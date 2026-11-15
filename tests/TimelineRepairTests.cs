using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static void RepairUnitTests() {
  var streams=new Metadata {VideoIndex=2,Fps=30000.0/1001,ServiceId=1056,SubtitleIndex=6,AudioTracks=new(){new(){Index=3},new(){Index=4}}};
  var remux=TimelineRepair.RemuxArguments(new Job {Input="input.ts",Metadata=streams},"output.ts");
  var maps=remux.Select((value,index)=>(value,index)).Where(x=>x.value=="-map").Select(x=>remux[x.index+1]).ToArray();
  Check(maps.SequenceEqual(new[]{"0:2","0:3","0:4","0:6"}),"timeline repair copies validated video, every valid audio and subtitle, excluding unvalidated PID 5");
  Check(!remux.Contains("0:p:1056")&&remux.IndexOf("-probesize")<remux.IndexOf("-i")&&remux.Contains("20000000"),"timeline repair uses bounded extended probing and explicit stream mapping");
  streams.AudioTracks.Clear();streams.SubtitleIndex=-1;
  remux=TimelineRepair.RemuxArguments(new Job {Input="input.ts",Metadata=streams},"output.ts");
  Check(remux.Count(x=>x=="-map")==1&&remux.Contains("0:2"),"video-only repair does not enable automatic audio mapping");
  Check(!Store.Json.Deserialize<Preset>("{}").RepairTimeline,"older presets keep repair disabled");
  var m=new Metadata {AudioTracks=new(){new(){Index=1,SourceIndex=4},new(){Index=2,SourceIndex=7}}};
  Check(AdvancedOptions.Audio(new Preset {AudioStreams="7,4"},m)[0].Index==2,"explicit audio selection follows original indices after remux");
  var decoded=new[]{0.0,1.0,2.0,3.0};var indexed=new[]{0.0,1.0,2.03,3.0};
  bool rejected=false;try{CmIndex.Align(indexed,decoded);}catch(InvalidDataException){rejected=true;}Check(rejected,"normal CM alignment remains strict");
  Check(CmIndex.Align(indexed,decoded,1.0/30)==0,"repair permits bounded one-frame damaged boundary");
  rejected=false;try{CmIndex.Align(new[]{0.0,1.0,2.1,3.0},decoded,1.0/30);}catch(InvalidDataException){rejected=true;}Check(rejected,"repair rejects larger native frame divergence");
  var many=System.Linq.Enumerable.Range(0,50).Select(i=>(double)i).ToArray();var shifted=System.Linq.Enumerable.Range(0,50).Select(i=>i+(i>0?.03:0)).ToArray();
  rejected=false;try{CmIndex.Align(shifted,many,1.0/30);}catch(InvalidDataException){rejected=true;}Check(rejected,"repair does not permit unbounded index mismatches");
 }
 static async Task RepairSynthetic(string[] args) {
  string folder=Path.GetFullPath(Option(args,"--repair-synthetic"));Directory.CreateDirectory(folder);
  var s=Integration.LoadSettings(Path.GetFullPath(Option(args,"--tool-data")));s.EncodeCacheFolder=Path.Combine(folder,"Cache");
  string input=Path.Combine(folder,"gap.ts");
  var generated=await Processes.Run(s.Ffmpeg,new[]{"-v","error","-y","-f","lavfi","-i","testsrc2=size=320x180:rate=30","-f","lavfi","-i","sine=frequency=1000:sample_rate=48000","-t","26","-filter_complex","[0:v]trim=duration=6,setpts=PTS+20*gte(T\\,3)/TB[v];[1:a]atrim=duration=6,asetpts=PTS+20*gte(T\\,3)/TB[a]","-map","[v]","-map","[a]","-fps_mode","passthrough","-c:v","mpeg2video","-c:a","aac","-mpegts_service_id","501",input},CancellationToken.None);
  Check(generated.ExitCode==0,"synthetic TS with clock gap generated");
  string fingerprint=Store.Fingerprint(input);
  foreach(int mode in new[]{0,1,2}) {
   bool amd=mode>0;
   var p=new Preset {RepairTimeline=true,KeepSubtitles=false,Engine=amd?Engine.VCEEncC:Engine.CPU,Speed=amd?"balanced":"ultrafast",Width=320,Height=180};
   var j=new Job {Input=input,Preset=p,OutputFolder=Path.Combine(folder,"Output"),FileName=mode==2?"AMD-gap-cut":amd?"AMD-gap":"CPU-gap",LogFile=Path.Combine(folder,"Data","Logs","encode-"+mode+".log")};Directory.CreateDirectory(Path.GetDirectoryName(j.LogFile));
   if(mode==2) {
    p.CmMode=CmMode.Cut;p.CmReview=true;
    bool review=false;try{await Converter.Convert(j,s,CancellationToken.None,Console.WriteLine,()=>{});}catch(CmReviewException){review=true;}
    Check(review,"repaired timeline enters CM review");
    string timeline=j.Commercials.TimelineIdentity;
    j.Commercials=Commercials.Build(new[]{new TimeRange {Start=2,End=3}},j.Metadata.Duration,j.Metadata.Fps,Store.Fingerprint(input));
    j.Commercials.TimelineIdentity=timeline;j.Commercials.Approved=j.Commercials.Reviewed=true;
   }
   await Converter.Convert(j,s,CancellationToken.None,Console.WriteLine,()=>{});
   Check(j.TimelineRepair.RemovedSeconds>19&&j.Metadata.Duration<7,"converter closes synthetic gap mode "+mode);
   Check(!Directory.Exists(Path.Combine(EncodeCache.Root(Path.Combine(folder,"Data"),s.EncodeCacheFolder),j.Id)),"successful repaired conversion removes managed cache "+amd);
   var decode=await Processes.Run(s.Ffmpeg,new[]{"-v","error","-xerror","-i",j.Output,"-map","0:v:0","-map","0:a:0","-f","null","-"},CancellationToken.None);
   Check(decode.ExitCode==0,"repaired synthetic final AV decodes "+amd);
   if(mode==2)Check(j.Commercials.OutputDuration>4.9&&j.Commercials.OutputDuration<5.1&&File.Exists(j.Output+".cm.json"),"AMD CM cut retains approved repaired timeline on retry");
  }
  Check(Store.Fingerprint(input)==fingerprint,"repair never modifies synthetic original");
 }
 static async Task RepairMappedRecording(string[] args) {
  string folder=Path.GetFullPath(Option(args,"--repair-mapped-recording"));Directory.CreateDirectory(folder);
  var s=Integration.LoadSettings(Path.GetFullPath(Option(args,"--tool-data")));s.EncodeCacheFolder=Path.Combine(folder,"Cache");
  var j=new Job {Input=Path.GetFullPath(Option(args,"--input")),Preset=new Preset {RepairTimeline=true,Engine=Engine.VCEEncC,Speed="balanced",Width=1280,Height=720,KeepSubtitles=false},OutputFolder=Path.Combine(folder,"Output"),FileName="mapped-recording",LogFile=Path.Combine(folder,"Logs","convert.log")};
  Directory.CreateDirectory(Path.GetDirectoryName(j.LogFile));string fingerprint=Store.Fingerprint(j.Input);
  j.Metadata=await MetadataReader.Read(j.Input,s,0,CancellationToken.None,Console.WriteLine);
  int tracks=j.Metadata.AudioTracks.Count;string subtitle=j.Metadata.SubtitleCodec;
  await TimelineRepair.Prepare(j,s,folder,CancellationToken.None,Console.WriteLine,()=>{});
  Check(j.Metadata.AudioTracks.Count==tracks&&j.Metadata.SubtitleCodec==subtitle,"real repair preserves valid audio tracks and subtitle");
  Check(TimelineRepair.CachedInput(j,s,folder)==j.MediaInput,"repaired cache resolves after stream indices are remapped");
  if(args.Contains("--repair-only")){Check(Store.Fingerprint(j.Input)==fingerprint,"repair-only original unchanged");return;}
  await Converter.Convert(j,s,CancellationToken.None,Console.WriteLine,()=>{});
  var decoded=await Processes.Run(s.Ffmpeg,new[]{"-v","error","-xerror","-i",j.Output,"-map","0:v:0","-map","0:a:0","-f","null","-"},CancellationToken.None);
  Check(decoded.ExitCode==0,"mapped recording AMD H264 MP4 fully decodes");
  Check(Store.Fingerprint(j.Input)==fingerprint,"mapped recording original unchanged");
 }
 static async Task RepairRecording(string[] args) {
  string folder=Path.GetFullPath(Option(args,"--repair-recording"));Directory.CreateDirectory(folder);
  var s=Integration.LoadSettings(Path.GetFullPath(Option(args,"--tool-data")));
  s.EncodeCacheFolder=folder;
  var j=Store.Load<Job>(Path.GetFullPath(Option(args,"--job")),()=>throw new Exception("job missing"));
  j.Preset.RepairTimeline=true;j.WorkingInput="";j.Metadata=await MetadataReader.Read(j.Input,s,0,CancellationToken.None,Console.WriteLine);
  string before=Store.Fingerprint(j.Input);
  await TimelineRepair.Prepare(j,s,folder,CancellationToken.None,Console.WriteLine,()=>{});
  Check(j.Metadata.Duration<5500&&j.Metadata.Duration>5300,"real recording missing time removed");
  Check(j.TimelineRepair.RemovedSeconds>1400,"real recording reports over 1400 missing seconds");
  Check(j.Metadata.Title.Contains("モーニングショー"),"original programme metadata retained");
  Check(j.Metadata.SubtitleCodec=="arib_caption","ARIB caption stream retained after correction");
  Check(TimelineRepair.CachedInput(j,s,folder)==j.MediaInput,"GUI resolves validated repaired cache");
  var copy=Store.Clone(j);Check(copy.WorkingInput==""&&copy.Input==j.Input,"runtime processing path is not persisted");
  Store.Save(Path.Combine(folder,"job-repaired.json"),j);
  // Exercise the same cache on retry, then CM's native index and complete alignment.
  var reused=File.GetLastWriteTimeUtc(j.MediaInput);j.Metadata=await MetadataReader.Read(j.Input,s,0,CancellationToken.None,Console.WriteLine);
  await TimelineRepair.Prepare(j,s,folder,CancellationToken.None,Console.WriteLine,()=>{});
  Check(File.GetLastWriteTimeUtc(j.MediaInput)==reused,"verified remux reused on retry");
  j.Preset.CmNoLogo=true;j.Preset.CmMode=CmMode.Chapters;j.Preset.CmSource=CmSource.Amatsukaze;j.Preset.CmReview=false;
  await Commercials.Prepare(j,s,folder,CancellationToken.None,Console.WriteLine,()=>{});
  Check(j.Commercials.Duration>5300&&j.Commercials.TimelineIdentity==Store.Fingerprint(j.MediaInput),"CM aligned to complete repaired timeline");
  Store.Save(Path.Combine(folder,"job-cm.json"),j);
  j.Preset.KeepSubtitles=true;j.Preset.IgnoreSubtitleErrors=true;
  string captions=await Subtitles.Prepare(j,s,folder,CancellationToken.None,Console.WriteLine,()=>{});
  Check(captions!=null&&new FileInfo(captions).Length>0,"real repaired ARIB captions extracted");
  foreach(double seconds in new[]{4250.0,4310.0,4350.0,5400.0}) {
   string clip=Path.Combine(folder,"clip-"+seconds+".ts");
   var r=await Processes.Run(s.Ffmpeg,new[]{"-v","error","-y","-ss",Commercials.N(seconds),"-i",j.MediaInput,"-t","12","-map","0:v:0","-map","0:a:0","-c","copy","-mpegts_service_id",j.Metadata.ServiceId.ToString(),"-f","mpegts",clip},CancellationToken.None);
   Check(r.ExitCode==0,"repaired join/late clip prepared "+seconds);
   var p=Store.Clone(j.Preset);p.RepairTimeline=false;p.CmMode=CmMode.Off;p.KeepSubtitles=false;
   var test=new Job {Input=clip,Preset=p,OutputFolder=Path.Combine(folder,"Output"),FileName="AMD-repaired-"+seconds,LogFile=Path.Combine(folder,"Data","Logs","encode-"+seconds+".log")};Directory.CreateDirectory(Path.GetDirectoryName(test.LogFile));
   await Converter.Convert(test,s,CancellationToken.None,Console.WriteLine,()=>{});
   var decode=await Processes.Run(s.Ffmpeg,new[]{"-v","error","-xerror","-i",test.Output,"-map","0:v:0","-map","0:a:0","-f","null","-"},CancellationToken.None);
   Check(decode.ExitCode==0,"actual AMD repaired timeline conversion and AV decode "+seconds);
  }
  Check(Store.Fingerprint(j.Input)==before,"original recording unchanged");
  Console.WriteLine("REPAIR RECORDING PASS");
 }
}
