using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static void MainEpgTests() {
  var start=new DateTime(2026,11,5,12,0,0);
  var previous=new EpgCandidate {Title="前番組",Start=start.AddHours(-1),Duration=3600,EventId=1};
  var main=new EpgCandidate {Title="本編",Start=start,Duration=1800,EventId=2};
  var after=new EpgCandidate {Title="次番組",Start=start.AddMinutes(30),Duration=1800,EventId=3};
  foreach(int pre in new[]{0,5,30,120,600})foreach(int post in new[]{0,15,120})Check(EpgSelection.Choose(new[]{previous,main,after},start.AddSeconds(-pre),pre+1800+post,main,previous)==main,"programme selection ignores fixed margin pre="+pre+" post="+post);
  Check(EpgSelection.Choose(new[]{previous,main},null,1800,main,previous)==main,"missing broadcast clock falls back to middle event");
  Check(EpgSelection.Choose(new[]{previous,main},start.AddYears(2),1800,main,previous)==main,"unrelated broadcast dates do not select schedule candidates");
  Check(EpgSelection.Choose(new[]{previous},null,0,null,previous)==previous,"missing middle preserves beginning fallback");
  Check(EpgSelection.Choose(Array.Empty<EpgCandidate>(),start,1800,null,null)==null,"missing EIT leaves filename fallback available");
  Check(EpgSelection.Choose(new[]{main,after},start,3600,after,main)==after,"equal overlap prefers event sampled at middle");
  Check(EpgSelection.Overlap(new EpgCandidate {Title="bad",Start=DateTime.MaxValue,Duration=60},start,60)==0,"invalid event end cannot overflow time selection");
  var clock=new EpgClock(256);clock.Add(PcrPacket(256,(1L<<33)-90000));clock.Add(PcrPacket(256,45000));clock.Add(PsiPacket(0x14,Tdt(start.AddSeconds(2)),0));
  Check(clock.RecordingStart.HasValue&&Math.Abs((clock.RecordingStart.Value-start).TotalSeconds-.5)<.001,"broadcast clock handles PCR wrap and initial table delay");
  var wrong=new EpgClock(300);wrong.Add(PcrPacket(256,0));wrong.Add(PsiPacket(0x14,Tdt(start),0));Check(!wrong.RecordingStart.HasValue,"other-service PCR does not anchor recording clock");
 }
 static byte Bcd(int n)=>(byte)((n/10)*16+n%10);
 static byte[] ClockBytes(DateTime t) {int mjd=(int)(t.Date-new DateTime(1858,11,17)).TotalDays;return new[]{(byte)(mjd>>8),(byte)mjd,Bcd(t.Hour),Bcd(t.Minute),Bcd(t.Second)};}
 static byte[] Tdt(DateTime t)=>new byte[]{0x70,0x70,5}.Concat(ClockBytes(t)).ToArray();
 static byte[] PcrPacket(int pid,long value) {
  byte[] p=Enumerable.Repeat((byte)255,188).ToArray();p[0]=0x47;p[1]=(byte)(pid>>8);p[2]=(byte)pid;p[3]=0x20;p[4]=183;p[5]=0x10;
  p[6]=(byte)(value>>25);p[7]=(byte)(value>>17);p[8]=(byte)(value>>9);p[9]=(byte)(value>>1);p[10]=(byte)(((value&1)<<7)|0x7e);p[11]=0;return p;
 }
 static byte[] PsiPacket(int pid,byte[] section,int cc) {
  if(section.Length>183)throw new Exception("fixture section too long");byte[] p=Enumerable.Repeat((byte)255,188).ToArray();p[0]=0x47;p[1]=(byte)(0x40|(pid>>8));p[2]=(byte)pid;p[3]=(byte)(0x10|(cc&15));p[4]=0;section.CopyTo(p,5);return p;
 }
 static byte[] Eit(int service,int eventId,DateTime start,int duration,string title,int next) {
  byte[] name=new byte[]{0x1b,0x28,0x4a}.Concat(Encoding.ASCII.GetBytes(title)).ToArray();
  var descriptor=new List<byte>{0x4d,(byte)(5+name.Length),(byte)'j',(byte)'p',(byte)'n',(byte)name.Length};descriptor.AddRange(name);descriptor.Add(0);
  var body=new List<byte>{(byte)(service>>8),(byte)service,0xc1,(byte)next,1,1,200,0,123,1,0x4e,(byte)(eventId>>8),(byte)eventId};body.AddRange(ClockBytes(start));body.AddRange(new[]{Bcd(duration/3600),Bcd(duration/60%60),Bcd(duration%60),(byte)(0x80|(descriptor.Count>>8)),(byte)descriptor.Count});body.AddRange(descriptor);
  int length=body.Count+4;var section=new List<byte>{0x4e,(byte)(0xb0|(length>>8)),(byte)length};section.AddRange(body);uint crc=0xffffffff;
  foreach(byte b in section){crc^=(uint)b<<24;for(int i=0;i<8;i++)crc=(crc&0x80000000)!=0?(crc<<1)^0x04c11db7:crc<<1;}
  section.AddRange(new[]{(byte)(crc>>24),(byte)(crc>>16),(byte)(crc>>8),(byte)crc});return section.ToArray();
 }
 static async Task MainEpgLive(string[] args) {
  string folder=Path.Combine(Path.GetFullPath(Option(args,"--main-epg-live")),"run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var s=Integration.LoadSettings(Path.GetFullPath(Option(args,"--tool-data")));
  Check(File.Exists(s.EpgDll),"real EDCB EPG decoder available");
  string clean=Path.Combine(folder,"clean.ts"),input=Path.Combine(folder,"with-epg.ts");
  var generation=await Processes.Run(s.Ffmpeg,new[]{"-v","error","-y","-f","lavfi","-i","testsrc2=size=320x180:rate=30","-t","6","-c:v","mpeg2video","-mpegts_flags","+nit","-mpegts_service_id","501","-mpegts_original_network_id","123","-mpegts_transport_stream_id","456","-f","mpegts",clean},CancellationToken.None);
  Check(generation.ExitCode==0,"EPG fixture video generated");var start=new DateTime(2026,11,5,12,0,0);int cc=0;bool injected=false;
  using(var from=File.OpenRead(clean))using(var to=File.Create(input)) {
   byte[] p=new byte[188];while(from.Read(p,0,188)==188){to.Write(p);
    int control=(p[3]>>4)&3;if(!injected&&(control==2||control==3)&&p[4]>=7&&(p[5]&16)!=0) {
     to.Write(PsiPacket(0x14,Tdt(start.AddSeconds(-2)),0));
     for(int repeat=0;repeat<3;repeat++){to.Write(PsiPacket(0x12,Eit(501,10,start.AddMinutes(-30),1800,"BEFORE",0),cc++));to.Write(PsiPacket(0x12,Eit(501,11,start,1800,"MAIN",1),cc++));to.Write(PsiPacket(0x12,Eit(502,99,start.AddSeconds(-2),7200,"OTHER",0),cc++));}injected=true;
    }
   }
  }
  Check(injected,"broadcast clock and real current/following EIT injected");string fingerprint=Store.Fingerprint(input);
  var m=await MetadataReader.Read(input,s,501,CancellationToken.None,Console.WriteLine);Store.Save(Path.Combine(folder,"metadata.json"),m);
  Check(m.Title.Normalize(NormalizationForm.FormKC).Contains("MAIN")&&m.EventId==11,"real native EPG read selects following main event instead of pre-roll title");
  Check(m.Start==start&&m.ServiceId==501&&m.NetworkId==123&&m.TransportId==456,"title event ID and programme time stay consistent and exclude other service");
  Check(m.Source.Contains("重複時間"),"selection records overlap-based reason");
  string withoutNit=Path.Combine(folder,"without-nit.ts");using(var from=File.OpenRead(input))using(var to=File.Create(withoutNit)){byte[] p=new byte[188];while(from.Read(p,0,188)==188)if((((p[1]&31)<<8)|p[2])!=0x10)to.Write(p);}
  var noNit=await MetadataReader.Read(withoutNit,s,501,CancellationToken.None,Console.WriteLine);Check(noNit.EventId==11&&noNit.Title.Normalize(NormalizationForm.FormKC).Contains("MAIN"),"EIT database fallback selects main programme when NIT was stripped");
  File.WriteAllText(input+".program.txt","2026/11/05(木) 12:00～12:00:06\nテスト局\nSIDECAR_TITLE\n\nOriginalNetworkID:123\nTransportStreamID:456\nServiceID:501\nEventID:777\n",new UTF8Encoding(false));
  var sidecar=await MetadataReader.Read(input,s,501,CancellationToken.None,Console.WriteLine);Check(sidecar.Title=="SIDECAR_TITLE"&&sidecar.EventId==777,"EDCB sidecar retains priority over inferred TS event");
  Check(Store.Fingerprint(input)==fingerprint,"EPG selection does not modify source TS");Console.WriteLine("MAIN EPG LIVE PASS");
 }
 static async Task MainEpgReal(string[] args) {
  string folder=Path.GetFullPath(Option(args,"--main-epg-real"));Directory.CreateDirectory(folder);var s=Integration.LoadSettings(Path.GetFullPath(Option(args,"--tool-data")));
  var original=Store.Load<Job>(Path.GetFullPath(Option(args,"--job")),()=>throw new Exception("missing job"));string before=Store.Fingerprint(original.Input);
  var m=await MetadataReader.Read(original.Input,s,original.Preset.ServiceId,CancellationToken.None,Console.WriteLine);
  var side=Store.Clone(m);m.Title="";m.EventId=0;m.Description="";
  var probe=await Processes.Run(s.Ffprobe,new[]{"-v","error","-read_intervals",Commercials.N(m.Duration/2)+"%+1","-select_streams",m.VideoIndex.ToString(),"-show_packets","-show_entries","packet=pos","-of","csv=p=0",original.Input},CancellationToken.None);
  long middle=-1;foreach(var line in probe.Output.Split('\n'))if(long.TryParse(line.Split(',')[0].Trim(),out long n)&&n>=0){middle=n;break;}
  NativeEpg.Fill(original.Input,s.EpgDll,m,CancellationToken.None,middle,Console.WriteLine);
  Check(!string.IsNullOrWhiteSpace(m.Title)&&m.EventId>0,"actual broadcast TS main event decoded without sidecar override");
  Check(m.ServiceId==side.ServiceId&&m.Source.Contains("重複時間"),"actual broadcast clock and selected service used");
  Check(m.Title==side.Title,"actual TS main title agrees with EDCB recording sidecar");
  Check(Store.Fingerprint(original.Input)==before,"real programme selection keeps original recording intact");Store.Save(Path.Combine(folder,"metadata.json"),m);Console.WriteLine("MAIN EPG REAL PASS");
 }
}
