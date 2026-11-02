using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static void QueueV061(string root) {
  var now=DateTime.Now;var waiting=new Job{State=State.Waiting,Created=now.AddDays(-2)};var running=new Job{State=State.Running,Created=now.AddDays(-1)};var waiting2=new Job{State=State.Waiting,Created=now};var history=new Job{State=State.Completed,Created=now.AddDays(1)};var pending=new Job{State=State.NeedsCmReview};
  var sorted=QueueOrder.Display(new[]{history,waiting,running,waiting2,pending});Check(sorted.SequenceEqual(new[]{running,waiting,waiting2,pending,history}),"running rows first and waiting rows follow actual scheduler insertion order");
  string data=Path.Combine(root,"BulkCancel");using(var q=new QueueManager(data)) {
   foreach(var state in new[]{State.Waiting,State.Running,State.NeedsInput,State.NeedsCmReview,State.NeedsLogo,State.NeedsLogoReview,State.Completed,State.Failed})q.Jobs.Add(new(){State=state});
   Check(q.CancelAll()==6&&q.Paused,"bulk cancellation stops all pending states and pauses new starts");Check(q.Jobs.Take(6).All(j=>j.State==State.Cancelled)&&q.Jobs[6].State==State.Completed&&q.Jobs[7].State==State.Failed,"bulk cancellation preserves completed and failed history");
  }
  using(var q=new QueueManager(data))Check(q.Jobs.Take(6).All(j=>j.State==State.Cancelled),"bulk cancellation survives worker restart");
  string inboxData=Path.Combine(root,"SubmissionOrder");var inputs=Enumerable.Range(0,3).Select(n=>Path.Combine(root,"order-"+n+".ts")).ToArray();foreach(var f in inputs)File.WriteAllText(f,"test");QueueManager.Submit(inboxData,inputs);using(var q=new QueueManager(inboxData)){q.Tick();Check(q.Jobs.Select(j=>j.Input).SequenceEqual(inputs),"multi-file submission retains selected file order through inbox");q.CancelAll();}
  var held=new TelecineSample {MatchFrames=240,Cycles=48,DuplicateCycles=47,RepairedFrames=24,CombedFrames=1};Check(Telecine.MatchFilm(held),"low motion anime can qualify through reconstructed fields and duplicate cycles");held.RepairedFrames=0;Check(!Telecine.MatchFilm(held),"static progressive duplicates alone cannot prove telecine");held.RepairedFrames=24;held.CombedFrames=60;Check(!Telecine.MatchFilm(held),"native interlaced residue prevents unsafe 24p classification");
  var sample=new TelecineSample();Telecine.MatchLine(sample,"SC:0 | COMBS: 4 140 8 180 190 (combpel=80) match=0 combed=NO");Telecine.MatchLine(sample,"1/5 frame drop:");Telecine.MatchLine(sample," #3: totdiff=00000001 maxbdiff=00000002 dup lowest [DROP]");Check(sample.RepairedFrames==1&&sample.MatchFrames==1&&sample.DuplicateCycles==1,"fieldmatch and decimate debug evidence parsed without false frame counts");
  string timeline=AdvancedFilters.Timeline(new(),new(){Width=320,Height=240,Fps=30000.0/1001});Check(timeline.Contains("fps=fps=")&&!timeline.Contains("STARTPTS"),"timestamp holes and delayed first frames retain the requested seek clock before GPU pipe");
  Check(AdvancedFilters.Rate(30000.0/1001)=="30000/1001"&&AdvancedFilters.Rate(60000.0/1001)=="60000/1001","GPU pipe uses bounded exact rational frame rates instead of overflowing decimal fractions");
  Check(!Subtitles.CanRetryIgnoring(new(){State=State.Failed,Preset=new(),Message="映像区間の長さが一致しません。"}),"video validation failure is never presented as a subtitle retry");
 }
 static async Task RealV061(string[] args) {
  string data=Path.GetFullPath(Option(args,"--out"));Directory.CreateDirectory(data);var s=Integration.LoadSettings(Path.GetFullPath(Option(args,"--tool-data")));object gate=new();Action<string> log=line=>{lock(gate)File.AppendAllText(Path.Combine(data,"run.log"),line+"\n");};
  if(args.Contains("--failed-job")) {
   var j=Store.Load<Job>(Path.GetFullPath(Option(args,"--failed-job")),()=>new());var part=new EncodePart {Start=360,End=480,Mode="preset"};string output=Path.Combine(data,"fixed-360-480.mkv"),fp=Store.Fingerprint(j.Input);if(File.Exists(output))File.Delete(output);await AdvancedEncoder.EncodeVideo(j,s,part,output,CancellationToken.None,log);await AdvancedEncoder.ValidateVideo(s,output,120,CancellationToken.None,true);Check(Store.Fingerprint(j.Input)==fp,"failed source TS remains unchanged");Console.WriteLine("FIXED PART VALIDATED "+output);return;
  }
  string input=Path.GetFullPath(Option(args,"--anime-file")),fingerprint=Store.Fingerprint(input);var preset=new Preset {Engine=Engine.VCEEncC,Speed="balanced",AutoInverseTelecine=true,PipeGpu=true,ResumeChunks=true,Width=1280,Height=720,KeepSubtitles=false,Threads=2};
  var job=new Job {Input=input,Preset=preset,OutputFolder=Path.Combine(data,"Output"),FileName="anime-tested",LogFile=Path.Combine(data,"Data","Logs","encode.log")};Directory.CreateDirectory(Path.GetDirectoryName(job.LogFile));
  if(args.Contains("--analyze-only")){job.Metadata=await MetadataReader.Read(input,s,0,CancellationToken.None,log);job.Telecine=await Telecine.Analyze(job,s,CancellationToken.None,log);Store.Save(Path.Combine(data,"analysis.json"),job.Telecine);Console.WriteLine(job.Telecine.Reason);foreach(var sample in job.Telecine.Samples)Console.WriteLine(sample.Start+" "+sample.Method);return;}
  DateTime update=DateTime.MinValue;await Converter.Convert(job,s,CancellationToken.None,log,()=>{if(DateTime.UtcNow>update){Console.WriteLine(job.Message+" "+job.Progress.ToString("F0")+"%");update=DateTime.UtcNow.AddSeconds(20);}});
  var decode=await Processes.Run(s.Ffmpeg,new[]{"-v","error","-xerror","-i",job.Output,"-f","null","-"},CancellationToken.None,null,240);Check(decode.ExitCode==0,"whole supplied anime output decodes completely");Check(Store.Fingerprint(input)==fingerprint,"whole anime verification never modifies original TS");Store.Save(Path.Combine(data,"result.json"),job);Console.WriteLine(job.Telecine.Reason);Console.WriteLine("ANIME OUTPUT "+job.Output);
 }
}
