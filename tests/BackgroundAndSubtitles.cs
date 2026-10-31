using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static async Task BackgroundAndSubtitles(string root) {
  var received=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
  string data=Path.Combine(root,"TrayTest");
  using(var bridge=new GuiBridge(data,page=>received.TrySetResult(page))) {
   Check(await GuiBridge.TryOpen(data,"字幕・DRCS"),"tray can activate GUI over current-user pipe");
   Check(await received.Task.WaitAsync(TimeSpan.FromSeconds(3))=="字幕・DRCS","tray activation preserves target page");
   Check(!await GuiBridge.TryOpen(data+"other","変換キュー"),"tray activation is isolated by settings folder");
  }
  string input=Path.Combine(root,"subtitle-test.ts");File.WriteAllText(input,"fixture");
  var j=new Job {Input=input,Preset=new Preset(),Metadata=new Metadata {SubtitleIndex=0,SubtitleCodec="arib_caption",Duration=10,Start=new DateTime(2026,10,31)}};
  var settings=new Settings {CaptionTool=Path.Combine(root,"missing-decoder.exe")};
  bool strict=false;try{await Subtitles.Prepare(j,settings,data,CancellationToken.None,_=>{},()=>{});}catch(FileNotFoundException){strict=true;}
  Check(strict,"subtitle errors stop conversion by default");
  j.Preset.IgnoreSubtitleErrors=true;
  Check(await Subtitles.Prepare(j,settings,data,CancellationToken.None,_=>{},()=>{})==null&&!string.IsNullOrEmpty(j.Metadata.SubtitleWarning),"ignore policy skips failed subtitles with warning");
  using(var cancelled=new CancellationTokenSource()) {cancelled.Cancel();bool propagated=false;try{await Subtitles.Prepare(j,settings,data,cancelled.Token,_=>{},()=>{});}catch(OperationCanceledException){propagated=true;}Check(propagated,"subtitle ignore never swallows cancellation");}
  string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Store.Fingerprint(input)+"|0")));
  string folder=Path.Combine(data,"Drcs","Scans",hash),glyphHash=new string('a',32)+"_1_2_3";
  var scan=new CaptionScan {SchemaVersion=2,SubtitlePackets=1};scan.Glyphs.Add(new DrcsGlyph {Hash=glyphHash,Image="glyph.bmp"});scan.Cues.Add(new CaptionCue {Pts=0,Duration=1000,Text="未対応{{DRCS:"+glyphHash+"}}"});Store.Save(Path.Combine(folder,"captions.json"),scan);settings.CaptionTool=Environment.ProcessPath;
  j.Preset.IgnoreSubtitleErrors=false;bool pending=false;try{await Subtitles.Prepare(j,settings,data,CancellationToken.None,_=>{},()=>{});}catch(DrcsPendingException){pending=true;}Check(pending,"strict DRCS policy still waits for input");
  j.Preset.IgnoreSubtitleErrors=true;var srt=await Subtitles.Prepare(j,settings,data,CancellationToken.None,_=>{},()=>{});
  Check(File.ReadAllText(srt).Contains("�")&&j.Metadata.SubtitleWarning.Contains("DRCS"),"ignore policy substitutes unresolved DRCS and preserves readable captions");
  int attempts=0;string subtitle=await Subtitles.Pack(j,settings,"video.mp4","captions.srt","output.mp4",CancellationToken.None,_=>{},c=>{
   attempts++;if(attempts==1)return Task.FromResult(new ProcessResult {ExitCode=1,Error="subtitle mux error"});
   Check(!c.Args.Contains("captions.srt")&&c.Args.Contains("-metadata"),"subtitle mux fallback retains program metadata");return Task.FromResult(new ProcessResult {ExitCode=0});
  });Check(subtitle==null&&attempts==2,"failed subtitle mux retries once without subtitles");
  bool failed=false;try{await Subtitles.Pack(j,settings,"video.mp4",null,"output.mp4",CancellationToken.None,_=>{},c=>Task.FromResult(new ProcessResult {ExitCode=1,Error="video error"}));}catch(InvalidDataException){failed=true;}Check(failed,"subtitle ignore never hides unrelated mux failures");
  Check(Store.Clone(j.Preset).IgnoreSubtitleErrors,"subtitle policy survives preset and BAT profile serialization");
  using(var queue=new QueueManager(Path.Combine(root,"RetrySubtitle"))) {
   queue.Paused=true;var job=queue.Enqueue(input);job.State=State.NeedsInput;Integration.Send(queue.DataFolder,"retry-ignore-subtitles",job.Id);queue.Tick();
   Check(job.State==State.Waiting&&job.Preset.IgnoreSubtitleErrors,"existing DRCS-waiting job can retry with ignore policy");
   Check(!queue.Settings.Presets[queue.Settings.SelectedPreset].IgnoreSubtitleErrors,"job override leaves saved preset unchanged");
  }
 }
}
