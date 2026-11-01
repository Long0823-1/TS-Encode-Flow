using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static int checks;
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
 static void Reject(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
 static async Task Live(string root,string[] args) {
  string ffmpeg=Store.FindTool("ffmpeg.exe"),ffprobe=Store.FindTool("ffprobe.exe");Check(File.Exists(ffmpeg)&&File.Exists(ffprobe),"FFmpeg tools found");
  string input=Path.Combine(root,"録画 & 日本語.ts");
  var generated=await Processes.Run(ffmpeg,new[]{"-hide_banner","-y","-f","lavfi","-i","testsrc2=size=720x480:rate=30000/1001","-f","lavfi","-i","sine=frequency=1000:sample_rate=48000","-t","3","-vf","setsar=32/27,setfield=tff","-c:v","mpeg2video","-b:v","2000k","-flags","+ilme+ildct","-c:a","aac","-b:a","128k","-mpegts_service_id","501","-f","mpegts",input},CancellationToken.None,null,60);
  Check(generated.ExitCode==0,"synthetic interlaced MPEG-TS generated");
  string sidecar=input+".program.txt";
  File.WriteAllText(sidecar,"2026/10/31(土) 13:00～13:03\r\nテスト放送局\r\nテスト番組 第12話 & 日本語\r\n\r\n説明\r\nOriginalNetworkID:123\r\nTransportStreamID:456\r\nServiceID:501\r\nEventID:789\r\n",Encoding.GetEncoding(932));
  var config=new Settings {Ffmpeg=ffmpeg,Ffprobe=ffprobe,OutputFolder=Path.Combine(root,"Output"),QuietSeconds=3,Parallel=2};
  string vce=args.SkipWhile(a=>a!="--vce").Skip(1).FirstOrDefault();
  var p=new Preset {Width=320,Height=180,Deinterlace=Deinterlace.YadifBob,Threads=2,Speed="ultrafast"};
  if(vce!=null){config.Vceenc=vce;p.Engine=Engine.VCEEncC;p.Speed="balanced";p.Name="AMD integration";}
  config.Presets=new System.Collections.Generic.List<Preset>{p};
  string data=Path.Combine(root,"Data");Directory.CreateDirectory(data);Store.Save(Path.Combine(data,"settings.json"),config);
  using(var q=new QueueManager(data)) {
   var first=q.Enqueue(input);string input2=Path.Combine(root,"second.ts");File.Copy(input,input2);File.Copy(sidecar,input2+".program.txt");var second=q.Enqueue(input2);
   Check(q.Enqueue(input)==null,"pending input deduplicated");
   using(var writer=File.Open(input,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite))Check(!Converter.WritableRecording(input),"active recording writer blocks conversion");
   var deadline=DateTime.UtcNow.AddMinutes(3);int peak=0;
   do{q.Tick();peak=Math.Max(peak,q.ActiveCount);await Task.Delay(500);}while(q.Snapshot().Any(j=>j.State==State.Waiting||j.State==State.Running)&&DateTime.UtcNow<deadline);
   foreach(var job in q.Snapshot())Console.WriteLine(job.State+": "+job.Message+" "+job.Output);
   Check(q.Snapshot().All(j=>j.State==State.Completed),"parallel jobs completed");Check(peak==2,"two workers actually active");
   Check(first.Output!=second.Output&&File.Exists(first.Output)&&File.Exists(second.Output),"same macro name collision preserved both outputs");
   Check(Path.GetFileName(first.Output).Contains("20261031_1300_テスト放送局_テスト番組"),"recording metadata used in output name");
   var probe=await Processes.Run(ffprobe,new[]{"-v","error","-select_streams","v:0","-show_entries","stream=width,height,r_frame_rate,codec_name,sample_aspect_ratio","-of","json",first.Output},CancellationToken.None,null,30);
   Console.WriteLine(probe.Output);var dataResult=Store.Json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(probe.Output);var video=MetadataReader.Obj(MetadataReader.Items(dataResult,"streams").First());
   Check(MetadataReader.Int(video,"width")==320&&MetadataReader.Int(video,"height")==180,"output resolution 320x180 verified");Check(Math.Abs(MetadataReader.Ratio(MetadataReader.Text(video,"r_frame_rate"))-60000.0/1001)<0.02,"Yadif bob output 59.94 fps verified");
   Check(File.Exists(input)&&File.Exists(input2),"original TS retained");Check(!Directory.EnumerateFiles(config.OutputFolder,"*.reserve").Any(),"output reservations released");
   Check(q.Enqueue(input,true)==null,"watcher does not repeat completed input");
  }
  // Cancellation must terminate the process tree, not leave an orphan encoder.
  using(var cancel=new CancellationTokenSource(900)) {
   bool cancelled=false;try{await Processes.Run(ffmpeg,new[]{"-hide_banner","-re","-f","lavfi","-i","testsrc2=size=320x180:rate=30","-t","60","-f","null","-"},cancel.Token);}catch(OperationCanceledException){cancelled=true;}
   Check(cancelled,"encoder cancellation observed");
  }
 }
 [STAThread]static int Main(string[] args) {
  if(args.Contains("--logo-library-live")){LogoLibraryLive(Path.GetFullPath(Option(args,"--logo-library-live")),Path.GetFullPath(Option(args,"--input")),Path.GetFullPath(Option(args,"--logo")),Path.GetFullPath(Option(args,"--tool-data"))).GetAwaiter().GetResult();return 0;}
  if(args.Contains("--cm-standalone")){string data=Path.GetFullPath(args[Array.IndexOf(args,"--cm-standalone")+1]),input=Path.GetFullPath(args[Array.IndexOf(args,"--input")+1]);var config=new Settings {Ffmpeg=Store.FindTool("ffmpeg.exe"),Ffprobe=Store.FindTool("ffprobe.exe"),ChapterExe=Path.GetFullPath(".local/amatsukaze-research/package/exe_files/chapter_exe.exe"),LogoFrame=Path.GetFullPath(".local/cm-only/logoframe.exe")};if(args.Contains("--tool-data"))config=Integration.LoadSettings(Path.GetFullPath(args[Array.IndexOf(args,"--tool-data")+1]));var p=new Preset {CmMode=CmMode.Chapters,CmSource=CmSource.Amatsukaze,CmNoLogo=args.Contains("--no-logo"),CmFallbackWithoutLogo=args.Contains("--logo-fallback")};var m=MetadataReader.Read(input,config,p.ServiceId,CancellationToken.None,Console.WriteLine).GetAwaiter().GetResult();var j=new Job {Id="standalone-qa",Input=input,Fingerprint=Store.Fingerprint(input),Preset=p,Metadata=m};if(args.Contains("--logo-area")){var a=Option(args,"--logo-area").Split(',').Select(int.Parse).ToArray();StationLogos.SaveArea(data,j,new LogoArea {X=a[0],Y=a[1],Width=a[2],Height=a[3]});}CmPlan plan;try{plan=CmAnalysis.Analyze(j,config,data,CancellationToken.None,Console.WriteLine,()=>Console.WriteLine(j.Message)).GetAwaiter().GetResult();}catch(LogoReviewException)when(args.Contains("--expect-logo-review")){if(j.LogoCandidate==null||StationLogos.Find(data,j)!=null)throw new Exception("unapproved candidate was registered");j.State=State.NeedsLogoReview;Store.Save(Path.Combine(data,"job.json"),j);Console.WriteLine("LOGO REVIEW PASS: candidate held without station registration");return 0;}Store.Save(Path.Combine(data,"plan.json"),plan);Console.WriteLine("PLAN "+plan.Segments.Count+" "+plan.OutputDuration);return 0;}
  if(args.Contains("--cm-remux")){string folder=Path.GetFullPath(args[Array.IndexOf(args,"--cm-remux")+1]);Directory.CreateDirectory(folder);var j=Store.Load<Job>(args[Array.IndexOf(args,"--job-file")+1],()=>throw new Exception("job missing"));string output=Path.Combine(folder,"CM-final.mp4");Subtitles.Pack(j,new Settings {Ffmpeg=Store.FindTool("ffmpeg.exe")},j.Output,null,output,CancellationToken.None,Console.WriteLine).GetAwaiter().GetResult();j.Output=output;Store.Save(Path.Combine(folder,"job.json"),j);return 0;}
  if(args.Contains("--cm-convert")){string data=Path.GetFullPath(args[Array.IndexOf(args,"--cm-convert")+1]),input=Path.GetFullPath(args[Array.IndexOf(args,"--input")+1]),planFile=args[Array.IndexOf(args,"--plan")+1];Directory.CreateDirectory(data);var plan=Store.Load<CmPlan>(planFile,()=>throw new Exception("plan missing"));plan.Approved=true;plan.Reviewed=true;var config=new Settings {Ffmpeg=Store.FindTool("ffmpeg.exe"),Ffprobe=Store.FindTool("ffprobe.exe"),Vceenc=Path.GetFullPath("../desktop-packages/vceenc/VCEEncC64.exe"),CaptionTool=Path.GetFullPath("build/release-v0.3.0/Tools/CaptionProbe.exe")};var preset=new Preset {Engine=Engine.VCEEncC,Width=1280,Height=720,CmMode=CmMode.Cut,KeepSubtitles=true,Speed="balanced"};var j=new Job {Input=input,Preset=preset,Commercials=plan,OutputFolder=Path.Combine(data,"Output"),FileName="CM-cut-{Title}",LogFile=Path.Combine(data,"Data","Logs","cm.log")};Directory.CreateDirectory(Path.GetDirectoryName(j.LogFile));Converter.Convert(j,config,CancellationToken.None,Console.WriteLine,()=>{}).GetAwaiter().GetResult();Store.Save(Path.Combine(data,"job.json"),j);Console.WriteLine("CM CONVERT OUTPUT "+j.Output);return 0;}
  if(args.Contains("--install-tools")){string data=Path.GetFullPath(args[Array.IndexOf(args,"--install-tools")+1]);var config=Integration.LoadSettings(data);config.OutputFolder=Path.Combine(data,"Output");foreach(var tool in ToolInstaller.Packages.Where(t=>!args.Contains("--tool")||t.Id==args[Array.IndexOf(args,"--tool")+1])){Console.WriteLine("INSTALL "+tool.Name);var receipt=ToolInstaller.Install(data,tool,CancellationToken.None,Console.WriteLine).GetAwaiter().GetResult();ToolInstaller.Apply(config,tool,receipt);Store.Save(Path.Combine(data,"settings.json"),config);Console.WriteLine("APPLIED "+receipt.Path+" "+receipt.Sha256+" license files="+ToolInstaller.LicenseFiles(data,tool).Length);}return 0;}

  string root=Path.Combine(Path.GetTempPath(),"TS-Encode-Flow-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try {
   foreach(string arg in new[]{"abc","日本語 & test","C:\\path with spaces\\","quote\"test","","C:\\a\\\"b"})Check(Processes.Split(Processes.Quote(arg)).Single()==arg,"Windows argument roundtrip: "+arg);
   var m=new Metadata {Title="番組/特集:その1",OriginalName="source",Channel="局",Start=new DateTime(2026,10,31,13,0,0),Width=1440,Height=1080,Sar=4.0/3};var p=new Preset {Width=1280,Height=720};
   Check(Commands.Resolution(p,m).SequenceEqual(new[]{1280,720}),"anamorphic 1440x1080 becomes 16:9 1280x720");
   Check(Names.Render("{Start:yyyyMMdd}/{Title}",m,p)==Path.Combine("20261031","番組_特集_その1.mp4"),"metadata sanitized without path injection");
   Reject(()=>Names.Render("../{Title}",m,p),"relative traversal rejected");Reject(()=>Names.Render("{Unknown}",m,p),"unknown macro rejected");
   Check(Names.Safe("CON.txt")=="_CON.txt","Windows reserved name handled");
   Reject(()=>Commands.Validate(new Preset {ExtraArgs="--output other.mp4"}),"extra output override rejected");Reject(()=>Commands.Validate(new Preset {Width=721}),"odd resolution rejected");Reject(()=>Commands.Validate(new Preset {Fps="30;calc"}),"invalid fps rejected");
   var small=new Metadata {Width=320,Height=240,Sar=1};Check(Commands.Resolution(new Preset {Width=1920,Height=1080},small).SequenceEqual(new[]{320,240}),"upscale disabled");
   Check(Commands.Filters(new Preset {Deinterlace=Deinterlace.YadifBob},m).Contains("send_field"),"bob selects field output");
   Check(Converter.EncoderProgress("[22.3%] 12666 frames, GPU 20%, VE 99%") == 22.3,"GPU progress reads encode percentage rather than GPU utilization");
   foreach(var engine in new[]{Engine.NVEncC,Engine.VCEEncC,Engine.QSVEncC}){var cmd=Commands.Hardware(new Settings(),new Preset {Engine=engine,Width=1280,Height=720},m,"in.ts","out.mp4");Check(cmd.Args.Contains("--vpp-yadif")&&cmd.Args.Contains("1280x720")&&cmd.Args.Contains(engine==Engine.QSVEncC?"--icq":"--cqp"),"hardware builder "+engine);}
   // Exercise the same inbox route as the UI, keeping conversion paused.
   string retryInput=Path.Combine(root,"retry.ts"),retryData=Path.Combine(root,"RetryQueue");File.WriteAllText(retryInput,"fixture");
   using(var retryQueue=new QueueManager(retryData)) {
    retryQueue.Paused=true;var cancelledJob=retryQueue.Enqueue(retryInput);retryQueue.Cancel(cancelledJob.Id);
    Check(cancelledJob.State==State.Cancelled,"queued job cancelled");
    Check(retryQueue.Enqueue(retryInput,true)==null,"watcher respects cancelled history");
    QueueManager.Submit(retryData,new[]{retryInput});retryQueue.Tick();
    var replacement=retryQueue.Jobs.Last();Check(replacement.Id!=cancelledJob.Id&&replacement.State==State.Waiting,"UI inbox accepts same TS after cancellation");
    QueueManager.Submit(retryData,new[]{retryInput});retryQueue.Tick();Check(retryQueue.Jobs.Count==2,"UI inbox still deduplicates waiting TS");
    replacement.State=State.Running;QueueManager.Submit(retryData,new[]{retryInput});retryQueue.Tick();Check(retryQueue.Jobs.Count==2,"UI inbox still deduplicates running TS");
    replacement.State=State.Completed;QueueManager.Submit(retryData,new[]{retryInput});retryQueue.Tick();Check(retryQueue.Jobs.Count==3,"UI inbox accepts intentional repeat after completion");
    Check(File.Exists(retryInput)&&cancelledJob.State==State.Cancelled,"retry preserves original TS and cancellation history");
   }
   string historyData=Path.Combine(root,"HistoryQueue");using(var queue=new QueueManager(historyData)){queue.Paused=true;var ended=queue.Enqueue(retryInput);ended.State=State.Completed;var pending=queue.Enqueue(retryInput);pending.State=State.NeedsCmReview;Check(queue.ClearHistory()==1&&queue.Jobs.Single().Id==pending.Id,"history clear retains review jobs and removes completed record");Check(queue.Enqueue(retryInput,true)==null,"history clear does not re-enqueue watched recording");Check(File.Exists(retryInput),"history clear preserves source TS");}
   using(var queue=new QueueManager(historyData)){queue.Paused=true;Check(queue.Enqueue(retryInput,true)==null,"watch deduplication survives history clear and restart");Check(queue.Enqueue(retryInput)!=null,"intentional manual re-encode allowed after history clear");}
   var deletion=new Settings();var customA=new Preset {Name="A",Engine=Engine.VCEEncC};var customB=new Preset {Name="B",Engine=Engine.VCEEncC};deletion.Presets.Insert(0,customA);deletion.Presets.Add(customB);deletion.SelectedPreset=deletion.BatPresetIndex=deletion.Presets.Count-1;PresetCatalog.DeleteCustom(deletion,customA);Check(ReferenceEquals(deletion.Presets[deletion.SelectedPreset],customB)&&ReferenceEquals(deletion.Presets[deletion.BatPresetIndex],customB),"custom deletion preserves selected and BAT preset references after index shift");PresetCatalog.DeleteCustom(deletion,customB);Check(deletion.Presets[deletion.SelectedPreset].Engine==Engine.VCEEncC&&deletion.BatPresetIndex==-1,"deleted selected preset falls back to same-engine factory and resets BAT selection");Reject(()=>PresetCatalog.DeleteCustom(deletion,deletion.Presets[0]),"factory presets cannot be deleted");
   var overwrite=new Settings();var duplicate1=new Preset {Name="マイ設定",Engine=Engine.CPU};var duplicate2=new Preset {Name="マイ設定",Engine=Engine.VCEEncC};overwrite.Presets.Add(duplicate1);overwrite.Presets.Add(duplicate2);overwrite.BatPresetIndex=overwrite.Presets.Count-1;int before=overwrite.Presets.Count;Check(PresetCatalog.SaveCustom(overwrite,new Preset {Name=" マイ設定 ",Engine=Engine.QSVEncC,Width=640,Height=360}),"same-name custom is overwritten across engines");Check(overwrite.Presets.Count==before-1&&overwrite.Presets.Count(p=>p.Name=="マイ設定")==1&&overwrite.Presets[overwrite.SelectedPreset].Width==640&&overwrite.BatPresetIndex==overwrite.SelectedPreset,"duplicate custom presets consolidate and BAT selection follows replacement");var factory=overwrite.Presets[0];PresetCatalog.SaveCustom(overwrite,new Preset {Name=factory.Name,Quality=12});Check(overwrite.Presets.Contains(factory)&&factory.Quality!=12,"same-name custom save protects factory presets");
   string autoData=Path.Combine(root,"AutoStartQueue");using(var queue=new QueueManager(autoData)){queue.Paused=true;QueueManager.Submit(autoData,new[]{retryInput});queue.Tick();Check(!queue.Paused&&queue.Jobs.Count==1&&queue.Jobs[0].State==State.Waiting,"explicit queue submission resumes processing while recently modified input waits safely");queue.Paused=true;queue.Tick();Check(queue.Paused,"explicit pause remains in effect until a new request");queue.Cancel(queue.Jobs[0].Id);queue.Retry(queue.Jobs[0].Id);Check(!queue.Paused&&queue.Jobs[0].State==State.Waiting,"retry resumes paused processing automatically");}
   string changeData=Path.Combine(root,"ChangePresetQueue");using(var queue=new QueueManager(changeData)){queue.Paused=true;var job=queue.Enqueue(retryInput);queue.Cancel(job.Id);job.Metadata=new Metadata();job.Commercials=new CmPlan();job.Glyphs.Add(new DrcsGlyph());string outputFolder=job.OutputFolder;var replacementPreset=new Preset {Name="個別設定",Engine=Engine.VCEEncC,Width=640,Height=360};Store.Save(Path.Combine(changeData,"Inbox","change.json"),new QueueManager.Request {Action="change-preset",JobId=job.Id,Profile=new BatchProfile {Preset=replacementPreset}});queue.Tick();Check(job.State==State.Cancelled&&job.Preset.Width==640&&job.Metadata==null&&job.Commercials==null&&job.Glyphs.Count==0&&job.OutputFolder==outputFolder,"cancelled job preset changes through worker inbox, resets analysis and stays stopped");replacementPreset.Width=320;Check(job.Preset.Width==640,"job preset replacement is an independent snapshot");queue.Retry(job.Id);Check(job.State==State.Waiting,"changed job starts only on explicit retry");Reject(()=>queue.ChangePreset(job.Id,new Preset()),"preset change rejects waiting or active jobs");}
   BackgroundAndSubtitles(root).GetAwaiter().GetResult();
   CmTests(root).GetAwaiter().GetResult();
   if(args.Contains("--cm-live"))CmLive(root,args).GetAwaiter().GetResult();
   UpdateTests().GetAwaiter().GetResult();
   if(args.Contains("--updates-live"))UpdateLive().GetAwaiter().GetResult();
   if(args.Contains("--live"))Live(root,args).GetAwaiter().GetResult();
   if(args.Contains("--features"))Features(root,args).GetAwaiter().GetResult();
   if(args.Contains("--real"))Real(root,args).GetAwaiter().GetResult();
   if(args.Contains("--user-file"))UserFile(args).GetAwaiter().GetResult();
   if(args.Contains("--metadata")) {
    string file=args[Array.IndexOf(args,"--metadata")+1],dll=args[Array.IndexOf(args,"--dll")+1];
    var settings=new Settings {Ffmpeg=Store.FindTool("ffmpeg.exe"),Ffprobe=Store.FindTool("ffprobe.exe"),EpgDll=dll};
    var info=MetadataReader.Read(file,settings,0,CancellationToken.None,Console.WriteLine).GetAwaiter().GetResult();Console.WriteLine(Store.Json.Serialize(info));
    Check(info.Title!=Path.GetFileNameWithoutExtension(file)&&System.Text.RegularExpressions.Regex.IsMatch(info.Title,@"[一-龥ぁ-んァ-ヶ]"),"Japanese program title decoded from real TS EIT");
    Check(info.Channel.Length>0&&info.EventId>0,"SDT channel and EIT event ID decoded");
   }
   Console.WriteLine("ALL PASS "+checks+"; fixtures: "+root);return 0;
  }catch(Exception e){Console.Error.WriteLine(e);Console.Error.WriteLine("Fixtures retained: "+root);return 1;}
 }
}
