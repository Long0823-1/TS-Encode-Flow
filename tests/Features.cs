using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static async Task Real(string root,string[] args) {
  var settings=new Settings {Ffmpeg=Store.FindTool("ffmpeg.exe"),Ffprobe=Store.FindTool("ffprobe.exe"),CaptionTool=Option(args,"--caption"),EpgDll=Option(args,"--dll"),QuietSeconds=3,OutputFolder=Path.Combine(root,"Output")};
  settings.Presets[0].Width=320;settings.Presets[0].Height=180;settings.Presets[0].Speed="ultrafast";settings.Presets[0].Threads=2;
  string data=Path.Combine(root,"Data");Store.Save(Path.Combine(data,"settings.json"),settings);
  using(var q=new QueueManager(data)) {
   var job=q.Enqueue(Option(args,"--real"));var deadline=DateTime.UtcNow.AddMinutes(2);
   do{q.Tick();await Task.Delay(500);}while((job.State==State.Waiting||job.State==State.Running)&&DateTime.UtcNow<deadline);
   Console.WriteLine(job.State+" "+job.Message+" "+job.Output);Check(job.State==State.Completed,"real broadcast TS conversion completed");
   var probe=await Processes.Run(settings.Ffprobe,new[]{"-v","error","-show_streams","-show_format","-of","json",job.Output},CancellationToken.None,null,30);var info=Store.Json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(probe.Output);
   Check(MetadataReader.Items(info,"streams").Select(MetadataReader.Obj).Any(s=>MetadataReader.Text(s,"codec_name")=="mov_text"),"real ARIB captions embedded as MP4 subtitle stream");
   var tags=MetadataReader.Obj(MetadataReader.Obj(info["format"])["tags"]);Check(tags.Any(t=>t.Key=="title"&&t.Value.ToString()==job.Metadata.Title),"real EIT program title embedded into video metadata");
  }
 }
 static string Option(string[] args,string name)=>args[Array.IndexOf(args,name)+1];
 static async Task Features(string root,string[] args) {
  var catalog=PresetCatalog.Create();Check(catalog.Count==48&&catalog.Select(p=>p.FactoryId).Distinct().Count()==48,"48 distinct content/engine presets");foreach(var p in catalog)Commands.Validate(p);
  Check(catalog.Count(p=>p.Category=="アニメ")==12&&catalog.Count(p=>p.Category=="映画")==8,"anime and movie presets available for all four engines");
  string caption=Option(args,"--caption"),worker=Option(args,"--worker");string scanFolder=Path.Combine(root,"DRCS");
  var native=await Processes.Run(caption,new[]{"--self-test",scanFolder},CancellationToken.None,null,30);Check(native.ExitCode==0,"native ARIB/DRCS decoder self-test");
  var scan=Store.Load<CaptionScan>(Path.Combine(scanFolder,"captions.json"),()=>throw new Exception());Check(scan.Cues.Count>0&&scan.Glyphs.Count>0,"DRCS bitmap and caption context extracted");
  Check(scan.Glyphs.All(g=>File.Exists(Path.Combine(scanFolder,g.Image))),"all DRCS glyph images exist");
  var glyph=scan.Glyphs.Last();glyph.Alternative="";var map=Drcs.Load(root);Check(!Drcs.Resolved(glyph,map),"unknown glyph requires manual input");Drcs.Save(root,glyph.Hash,"凛");map=Drcs.Load(root);Check(Drcs.Resolved(glyph,map)&&map[glyph.Hash].Text=="凛","manual glyph mapping persisted and reloaded");
  var metadata=new Metadata {Title="日本語番組 第12話",Channel="テスト局",Description="番組説明と放送情報",Episode="12",Start=new DateTime(2026,10,31,21,0,0),Duration=4,ServiceId=1,EventId=123};
  string text=Subtitles.Srt(scan,metadata,map);Check(text.Contains("凛")&&!text.Contains("{{DRCS:"),"DRCS substitution applied to subtitle text");
  string srt=Path.Combine(root,"字幕.srt");File.WriteAllText(srt,text,new UTF8Encoding(false));
  var settings=new Settings {Ffmpeg=Store.FindTool("ffmpeg.exe"),Ffprobe=Store.FindTool("ffprobe.exe")};string video=Path.Combine(root,"video.mp4");
  var generated=await Processes.Run(settings.Ffmpeg,new[]{"-hide_banner","-y","-f","lavfi","-i","testsrc2=size=320x180:rate=30","-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-t","4","-c:v","libx264","-preset","ultrafast","-c:a","aac",video},CancellationToken.None,null,40);Check(generated.ExitCode==0,"subtitle mux test video generated");
  var fallbackJob=new Job {Preset=new Preset {IgnoreSubtitleErrors=true},Metadata=metadata};string fallbackOutput=Path.Combine(root,"subtitle-fallback.mp4");
  string retained=await Subtitles.Pack(fallbackJob,settings,video,Path.Combine(root,"missing.srt"),fallbackOutput,CancellationToken.None,_=>{});
  Check(retained==null&&File.Exists(fallbackOutput)&&!string.IsNullOrEmpty(fallbackJob.Metadata.SubtitleWarning),"actual FFmpeg subtitle failure falls back to video with warning");
  var fallbackProbe=await Processes.Run(settings.Ffprobe,new[]{"-v","error","-show_streams","-show_format","-of","json",fallbackOutput},CancellationToken.None,null,30);var fallbackInfo=Store.Json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(fallbackProbe.Output);
  Check(MetadataReader.Items(fallbackInfo,"streams").Select(MetadataReader.Obj).Any(x=>MetadataReader.Text(x,"codec_type")=="video")&&MetadataReader.Items(fallbackInfo,"streams").Select(MetadataReader.Obj).Any(x=>MetadataReader.Text(x,"codec_type")=="audio"),"actual subtitle fallback preserves video and audio");
  Check(MetadataReader.Text(MetadataReader.Obj(MetadataReader.Obj(fallbackInfo["format"])["tags"]),"title")==metadata.Title,"actual subtitle fallback preserves Japanese metadata");
  foreach(Container container in Enum.GetValues(typeof(Container))) {
   string output=Path.Combine(root,"with-subtitles."+container);var command=Subtitles.Mux(settings,new Preset {Container=container},metadata,video,srt,output);var muxed=await Processes.Run(command.Exe,command.Args,CancellationToken.None,null,40);Check(muxed.ExitCode==0,"subtitle and metadata mux "+container);
   var probe=await Processes.Run(settings.Ffprobe,new[]{"-v","error","-show_streams","-show_format","-of","json",output},CancellationToken.None,null,30);
   var info=Store.Json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(probe.Output);var subtitle=MetadataReader.Items(info,"streams").Select(MetadataReader.Obj).FirstOrDefault(s=>MetadataReader.Text(s,"codec_type")=="subtitle");
   Check(subtitle!=null&&MetadataReader.Text(subtitle,"codec_name")== (container==Container.mp4?"mov_text":"ass"),"switchable subtitle stream verified "+container);
   var tags=MetadataReader.Obj(MetadataReader.Obj(info["format"])["tags"]);Check(tags.Any(t=>t.Key.Equals("title",StringComparison.OrdinalIgnoreCase)&&t.Value.ToString()==metadata.Title),"Japanese program title metadata verified "+container);Check(tags.Any(t=>t.Key.Equals("description",StringComparison.OrdinalIgnoreCase)&&t.Value.ToString()==metadata.Description),"program description metadata verified "+container);
   string exported=Path.Combine(root,container+"-captions.srt");var extract=await Processes.Run(settings.Ffmpeg,new[]{"-v","error","-y","-i",output,"-map","0:s:0","-c:s","srt",exported},CancellationToken.None,null,30);Check(extract.ExitCode==0&&File.ReadAllText(exported).Contains("凛"),"saved subtitle replacement roundtrip "+container);
  }
  // Use the actual generated .bat and a filename containing cmd metacharacters.
  string input=Path.Combine(root,"録画 & 名前 !%PATH% 字.ts");var remux=await Processes.Run(settings.Ffmpeg,new[]{"-v","error","-y","-i",video,"-c","copy","-f","mpegts",input},CancellationToken.None,null,30);Check(remux.ExitCode==0,"BAT input fixture created");
  string data=Path.Combine(root,"WorkerData");settings.QuietSeconds=3;settings.OutputFolder=Path.Combine(root,"BatchOutput");settings.BatPresetIndex=1;settings.Presets[1].Width=160;settings.Presets[1].Height=90;settings.Presets[1].Speed="ultrafast";Store.Save(Path.Combine(data,"settings.json"),settings);
  string bat=Path.Combine(root,"record-hook.bat");Integration.SaveBat(bat,worker,data,settings,settings.Presets[1]);
  settings.Presets[1].Width=256;settings.Presets[1].Height=144;Store.Save(Path.Combine(data,"settings.json"),settings);
  Check(File.Exists(bat+".profile.json")&&File.ReadAllText(bat).Contains("_EDCBX_DIRECT_"),"BAT and frozen preset profile generated");
  var psi=new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec"),"/d /s /c \"\""+bat+"\"\""){UseShellExecute=false,CreateNoWindow=true};psi.Environment["FilePath"]=input;
  using(var process=Process.Start(psi)){Check(process.WaitForExit(15000)&&process.ExitCode==0,"EPGTimer-style BAT accepted special-character TS path");}
  var deadline=DateTime.UtcNow.AddSeconds(60);System.Collections.Generic.List<Job> jobs;
  do{await Task.Delay(500);jobs=Integration.ReadJobs(data);}while((jobs.Count==0||jobs.Any(j=>j.State==State.Waiting||j.State==State.Running))&&DateTime.UtcNow<deadline);
  Check(jobs.Count==1&&jobs[0].State==State.Completed,"background worker completed BAT-submitted TS");Check(jobs[0].Input==input&&jobs[0].Preset.Width==160&&jobs[0].Preset.Height==90,"BAT retained literal path and preset snapshot");
  Integration.Send(data,"shutdown");var stopDeadline=DateTime.UtcNow.AddSeconds(10);do{await Task.Delay(250);}while(Integration.ReadStatus(data).Running&&DateTime.UtcNow<stopDeadline);Check(!Integration.ReadStatus(data).Running,"resident BAT worker shuts down cleanly");
 }
}
