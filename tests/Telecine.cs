using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static void TelecineTests() {
  string[] pattern={"neither","neither","top","neither","bottom"};
  Check(Telecine.Classify(Enumerable.Range(0,300).Select(i=>pattern[i%5])).Film,"3:2 cadence detected");
  Check(!Telecine.Classify(Enumerable.Repeat("neither",300)).Film,"native interlaced and progressive without repeated fields rejected");
  Check(!Telecine.Classify(Enumerable.Repeat("top",300)).Film,"static repeated field rejected");
  Check(!Telecine.Classify(Enumerable.Range(0,300).Select(i=>i%2==0?"top":"bottom")).Film,"30p duplicate animation cadence rejected");
  Check(!Telecine.Classify(pattern).Film,"short evidence rejected");
  var p=new Preset {AutoInverseTelecine=true,Fps="24"};Reject(()=>Commands.Validate(p),"automatic IVTC conflicts with manual FPS");p.Fps="";
  p.ExtraArgs="-r:v 30";Reject(()=>Commands.Validate(p),"stream-qualified extra FPS cannot bypass IVTC conflict check");p.ExtraArgs="";
  var m=new Metadata {Width=1440,Height=1080,Fps=30000.0/1001,Duration=600,InverseTelecine=true};
  var filters=Commands.Filters(p,m);Check(filters.Contains("fieldmatch=")&&filters.Contains("decimate=cycle=5")&&!filters.Contains("yadif"),"IVTC replaces normal deinterlace");
  m.InverseTelecine=false;Check(Commands.Filters(p,m).Contains("yadif")&&!Commands.Filters(p,m).Contains("decimate"),"uncertain detection retains preset filter");
  var windows=Telecine.Windows(m,new(){new(){Start=100,End=200},new(){Start=300,End=400}});
  Check(windows.Count>=3&&windows.All(w=>w.Start>=100&&w.End<=200||w.Start>=300&&w.End<=400),"sampling stays inside retained main program ranges");
  Check(Telecine.Windows(new(){Duration=12},null).Count==0,"insufficient independent duration skipped");
  Check(Store.Clone(p).AutoInverseTelecine&&!new Preset().AutoInverseTelecine,"preset option persists and defaults off");
 }
 static async Task TelecineLive(string root,string[] args) {
  var s=Integration.LoadSettings(Path.GetFullPath(Option(args,"--tool-data")));s.OutputFolder=Path.Combine(root,"IvtcOutput");
  foreach(string kind in new[]{"film","interlaced","progressive","mixed"}) {
   string input=Path.Combine(root,"ivtc-"+kind+".ts");
   string rate=kind=="film"?"24000/1001":kind=="interlaced"?"60000/1001":"30000/1001";
   string vf=kind=="film"?"telecine=pattern=23":kind=="interlaced"?"tinterlace=interleave_top":"null";
   string[] gen={"-hide_banner","-y","-f","lavfi","-i","testsrc2=size=320x240:rate="+rate,"-f","lavfi","-i","sine=frequency=600:sample_rate=48000","-t","60","-vf",vf,"-c:v","mpeg2video","-q:v","2","-c:a","aac",input};
   var generated=await Processes.Run(s.Ffmpeg,gen,CancellationToken.None,null,120);Check(generated.ExitCode==0,"IVTC fixture generated "+kind);
   if(kind=="mixed") {
    string film=Path.Combine(root,"ivtc-film.ts"),other=Path.Combine(root,"ivtc-interlaced.ts");
    File.WriteAllText(input+".txt","file '"+film.Replace('\\','/')+"'\nfile '"+other.Replace('\\','/')+"'\n");
    var concat=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-y","-f","concat","-safe","0","-i",input+".txt","-c","copy",input},CancellationToken.None,null,60);Check(concat.ExitCode==0,"mixed film and native interlace fixture");
   }
   var p=new Preset {AutoInverseTelecine=true,KeepSubtitles=false,EmbedMetadata=false,Speed="ultrafast",Threads=2};
   var j=new Job {Input=input,Preset=p,OutputFolder=s.OutputFolder,FileName=kind,LogFile=Path.Combine(root,"Data","Logs",kind+".log")};Directory.CreateDirectory(Path.GetDirectoryName(j.LogFile));
   await Converter.Convert(j,s,CancellationToken.None,line=>File.AppendAllText(j.LogFile,line+"\n"),()=>{});
   Check(j.Telecine.Applied==(kind=="film"),"actual TS classification "+kind);
   var probe=await Processes.Run(s.Ffprobe,new[]{"-v","error","-select_streams","v:0","-show_entries","stream=avg_frame_rate","-of","default=nw=1:nk=1",j.Output},CancellationToken.None,null,30);
   Check(Math.Abs(MetadataReader.Ratio(probe.Output.Trim())-(kind=="film"?24000.0/1001:30000.0/1001))<0.05,"output FPS verified "+kind);
   var decode=await Processes.Run(s.Ffmpeg,new[]{"-v","error","-xerror","-i",j.Output,"-f","null","-"},CancellationToken.None,null,120);Check(decode.ExitCode==0,"full output decodes "+kind);
   if(kind=="film") {
    // CM cuts must precede cadence conversion; the GPU receives already cut
    // audio/video instead of trimming 23.976fps with 29.97fps frame numbers.
    File.WriteAllText(input+".edl","10 20 0\n");p.CmMode=CmMode.Cut;p.CmReview=false;
    if(args.Contains("--vce")){p.Engine=Engine.VCEEncC;p.Speed="balanced";s.Vceenc=Option(args,"--vce");}
    var cut=new Job {Input=input,Preset=p,OutputFolder=s.OutputFolder,FileName="film-cut",LogFile=Path.Combine(root,"Data","Logs","cut.log")};
    await Converter.Convert(cut,s,CancellationToken.None,line=>File.AppendAllText(cut.LogFile,line+"\n"),()=>{});
    Check(cut.Telecine.Applied&&File.Exists(cut.Output),"CM cut plus IVTC completed "+p.Engine);
   }
  }
 }
}
