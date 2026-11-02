using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public static class SignalDiagnostics {
  public static void Issue(SignalReport r,double time,string kind,string detail,long offset=-1,bool approximate=false) {
   if(r.Issues.Count>=10000){r.Truncated=true;return;}r.Issues.Add(new(){Seconds=Math.Max(0,time),Kind=kind,Detail=detail,Offset=offset,Approximate=approximate});
  }
  public static SignalReport Transport(string file,double duration,CancellationToken token,Action<double> progress=null) {
   var r=new SignalReport {Fingerprint=Store.Fingerprint(file)};var counters=new Dictionary<int,int>();var previous=new Dictionary<int,byte[]>();using var input=File.OpenRead(file);byte[] packet=new byte[188];
   double firstPcr=-1,lastPcr=-1,wrap=0,prior=-1;const double period=(1L<<33)/90000.0;
   while(input.ReadExactlyOrEnd(packet)) {
    long offset=input.Position-188;if(r.Packets%16384==0){token.ThrowIfCancellationRequested();progress?.Invoke((double)input.Position/input.Length);}
    bool approximate=firstPcr<0;double time=approximate?duration*offset/input.Length:Math.Max(0,lastPcr-firstPcr);
    if(packet[0]!=0x47){r.SyncErrors++;Issue(r,time,"同期ずれ","TSパケット境界の再同期",offset,true);input.Position=offset+1;continue;}
    r.Packets++;int pid=((packet[1]&31)<<8)|packet[2],control=(packet[3]>>4)&3,cc=packet[3]&15;
    if(pid==8191)continue;
    if((packet[1]&128)!=0){r.TransportErrors++;Issue(r,time,"TEI","受信エラーフラグ PID="+pid,offset,approximate);}
    bool reset=false;
    if((control==2||control==3)&&packet[4]>=1&&packet[4]<=183) {
     reset=(packet[5]&128)!=0;
     if((packet[5]&16)!=0&&packet[4]>=7){double pcr=((long)packet[6]<<25|((long)packet[7]<<17)|((long)packet[8]<<9)|((long)packet[9]<<1)|(long)(packet[10]>>7))/90000.0;if(prior>=0&&pcr<prior-period/2)wrap+=period;prior=pcr;lastPcr=pcr+wrap;if(firstPcr<0)firstPcr=lastPcr;}
    }
    if(reset){counters.Remove(pid);previous.Remove(pid);}
    if(control!=1&&control!=3)continue;
    if(counters.TryGetValue(pid,out int old)&&cc!=((old+1)&15)&&!(cc==old&&previous.TryGetValue(pid,out var bytes)&&bytes.SequenceEqual(packet))) {r.ContinuityErrors++;Issue(r,time,"パケット欠落候補","PID="+pid+" CC "+old+"→"+cc,offset,approximate);}
    counters[pid]=cc;if(!previous.TryGetValue(pid,out var storage)){storage=new byte[188];previous[pid]=storage;}Array.Copy(packet,storage,188);
   }
   if(Store.Fingerprint(file)!=r.Fingerprint)throw new IOException("診断中にTSが更新されました。");return r;
  }
  static bool ReadExactlyOrEnd(this Stream stream,byte[] b){int n=0;while(n<b.Length){int read=stream.Read(b,n,b.Length-n);if(read==0)return false;n+=read;}return true;}
  public static async Task<SignalReport> Analyze(string file,Metadata m,Settings s,CancellationToken token,Action<string> log,Action<double> progress=null) {
   var report=await Task.Run(()=>Transport(file,m.Duration,token,progress),token);report.VideoIndex=m.VideoIndex;report.AudioIndices=string.Join(",",m.AudioTracks.Select(x=>x.Index));var prior=new Dictionary<int,double>();
   var result=await Processes.Run(s.Ffprobe,new[]{"-v","error","-show_packets","-show_entries","packet=stream_index,dts_time,duration_time,flags","-of","compact=p=0:nk=0",file},token,line=>{
    var values=new Dictionary<string,string>();foreach(var pair in line.Split('|').Select(x=>x.Split('=',2)).Where(x=>x.Length==2))values[pair[0]]=pair[1];
    if(!values.TryGetValue("stream_index",out var stream)||!int.TryParse(stream,out int index)||!values.TryGetValue("dts_time",out var stamp)||stamp=="N/A")return;
    if(index!=m.VideoIndex&&!m.AudioTracks.Any(a=>a.Index==index)&&index!=m.AudioIndex)return;
    double time=MetadataReader.Number(stamp);if(prior.TryGetValue(index,out double end)&&(time-end>0.5||time<end-1)){report.TimestampGaps++;Issue(report,time-m.VideoStart,index==m.VideoIndex?"映像時刻の飛び":"音声時刻の飛び","stream="+index+" 差="+Commercials.N(time-end)+"秒");}
    prior[index]=time+(values.TryGetValue("duration_time",out var duration)?MetadataReader.Number(duration):0);
   });
   if(result.ExitCode!=0)Issue(report,0,"解析警告",Converter.Tail(result.Error));if(Store.Fingerprint(file)!=report.Fingerprint)throw new IOException("診断中に入力が更新されました。");
   log("TS診断：CC="+report.ContinuityErrors+" TEI="+report.TransportErrors+" 同期="+report.SyncErrors+" 時刻飛び="+report.TimestampGaps);return report;
  }
 }
}
