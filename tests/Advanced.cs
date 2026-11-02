using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static void AdvancedTests(string root) {
  var p=new Preset();Check(!AdvancedOptions.Enabled(p),"advanced pipeline is opt-in for old presets");
  Check(AdvancedOptions.Enabled(new(){Dither=DitherMode.Bayer}),"explicit dither selection activates actual color processing");
  p.BitDepth=10;p.Engine=Engine.VCEEncC;Reject(()=>Commands.Validate(p),"GPU H264 10bit rejected before execution");p.Codec=Codec.HEVC;Commands.Validate(p);
  p.Cadence=CadenceMode.MixedVfr;p.Fps="30";Reject(()=>Commands.Validate(p),"VFR rejects fixed FPS");p.Fps="";p.ExtraArgs="--fps 30";Reject(()=>Commands.Validate(p),"advanced pipeline rejects conflicting custom arguments");p.ExtraArgs="";
  var m=new Metadata {Width=640,Height=360,Fps=30000.0/1001,Duration=36,AudioIndex=1,AudioTracks=new(){new(){Index=1,Channels=2},new(){Index=2,Channels=6}}};
  Check(Math.Abs(AdvancedEncoder.SourceStart(new(){VideoStart=2.1,FormatStart=1.4},10)-10.7)<0.00001,"video-relative chunk seek accounts for earlier audio/container start");
  p.AllAudio=true;Check(AdvancedOptions.Audio(p,m).Count==2,"all audio streams selected from service");p.AudioStreams="2";Check(AdvancedOptions.Audio(p,m).Single().Channels==6,"explicit audio selection retains layout");p.AudioStreams="3";Reject(()=>AdvancedOptions.Audio(p,m),"unrelated service stream cannot be selected");p.AudioStreams="1,1";Reject(()=>AdvancedOptions.Audio(p,m),"duplicate audio mapping rejected");p.AudioStreams="";
  var j=new Job {Metadata=m,Preset=p};var parts=AdvancedEncoder.Parts(j);Check(parts.Count==3&&parts.Last().End==36,"mixed cadence splits source into bounded 12 second intervals");
  p.CmMode=CmMode.Cut;j.Commercials=Commercials.Build(new[]{new TimeRange {Start=10,End=20}},36,m.Fps,"test");parts=AdvancedEncoder.Parts(j);Check(parts.All(x=>j.Commercials.Keep.Any(k=>x.Start>=k.Start&&x.End<=k.End))&&Math.Abs(parts.Sum(x=>x.End-x.Start)-26)<0.05,"advanced parts never encode removed commercials");
  p.ColorMatrix=VideoMatrix.Bt2020nc;Reject(()=>AdvancedFilters.Video(p,m),"unknown input color cannot silently become a different gamut");p.InputColorMatrix=VideoMatrix.Bt709;AdvancedFilters.InputColor(p,m);Check(AdvancedFilters.Video(p,m).Contains("colorspace=all=bt2020"),"explicit color conversion includes input interpretation");
  string ts=Path.Combine(root,"damaged.ts");byte[] packet=new byte[188];packet[0]=0x47;packet[1]=0;packet[2]=100;packet[3]=0x10;using(var f=File.Create(ts)){f.Write(packet);packet[3]=0x12;f.Write(packet);packet[1]=0x80;packet[3]=0x13;f.Write(packet);}
  var signal=SignalDiagnostics.Transport(ts,3,CancellationToken.None);Check(signal.ContinuityErrors==1&&signal.TransportErrors==1,"TS continuity gaps and TEI are counted independently");
  Check(signal.Issues.All(x=>x.Approximate),"packet-position time estimates are explicitly marked approximate");
  var clone=Store.Clone(p);Check(clone.AllAudio&&clone.Cadence==CadenceMode.MixedVfr&&clone.InputColorMatrix==VideoMatrix.Bt709,"advanced preset fields survive BAT snapshots");
  string cacheId=Guid.NewGuid().ToString("N"),cache=Path.Combine(root,"EncodeCache",cacheId,"key");Directory.CreateDirectory(cache);File.WriteAllText(Path.Combine(cache,"part.mkv"),"owned");string source=Path.Combine(root,"keep.ts");File.WriteAllText(source,"source");EncodeCache.Clear(root,cacheId);Check(!Directory.Exists(cache)&&File.Exists(source),"managed cache deletion preserves source outside its scope");Reject(()=>EncodeCache.Clear(root,"../"),"cache deletion rejects traversal identifiers");
 }
 static async Task AdvancedLive(string root,string[] args) {
  var s=Integration.LoadSettings(Path.GetFullPath(Option(args,"--tool-data")));string input=Path.Combine(root,"advanced-input.ts");
  s.EncodeCacheFolder=Path.Combine(root,"UserSelectedCache");
  var gen=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-y","-f","lavfi","-i","testsrc2=size=320x240:rate=24000/1001","-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-f","lavfi","-i","anullsrc=r=48000:cl=5.1","-t","36","-vf","telecine=pattern=23","-map","0:v","-map","1:a","-map","2:a","-c:v","mpeg2video","-q:v","2","-color_primaries","bt709","-color_trc","bt709","-colorspace","bt709","-c:a","aac","-metadata:s:a:0","language=jpn","-metadata:s:a:1","language=eng",input},CancellationToken.None,null,120);Check(gen.ExitCode==0,"advanced dual-audio film TS fixture");
  var m=await MetadataReader.Read(input,s,0,CancellationToken.None,_=>{});Check(m.AudioTracks.Count==2&&m.AudioTracks[1].Language=="eng","metadata enumerates selected-service audio and language");
  string cancelData=Path.Combine(root,"CancelAllLive");using(var queue=new QueueManager(cancelData)) {
   queue.Settings=new Settings {Ffmpeg=s.Ffmpeg,Ffprobe=s.Ffprobe,Parallel=2,QuietSeconds=3,OutputFolder=Path.Combine(root,"CancelAllOutput"),Presets=new(){new(){KeepSubtitles=false,Speed="ultrafast",Threads=1}}};
   for(int i=0;i<2;i++){string copy=Path.Combine(root,"cancel-all-"+i+".ts");File.Copy(input,copy);File.SetLastWriteTimeUtc(copy,DateTime.UtcNow.AddMinutes(-2));queue.Enqueue(copy);}queue.Tick();Check(queue.ActiveCount==2,"bulk cancel test has two actual active conversion tasks");Check(queue.CancelAll()==2,"bulk cancel signals both active cancellation tokens");var deadline=DateTime.UtcNow.AddSeconds(15);while(queue.ActiveCount>0&&DateTime.UtcNow<deadline)await Task.Delay(50);Check(queue.ActiveCount==0&&queue.Jobs.All(j=>j.State==State.Cancelled)&&queue.Paused,"bulk cancellation waits for process cleanup and preserves paused state");
  }
  var p=new Preset {ResumeChunks=true,ChunkSeconds=10,AllAudio=true,KeepSubtitles=false,Speed="ultrafast",Threads=2};
  if(args.Contains("--vce")){p.Engine=Engine.VCEEncC;p.Speed="balanced";p.PipeGpu=true;s.Vceenc=Option(args,"--vce");}
  var j=new Job {Input=input,Metadata=m,Preset=p,Fingerprint=Store.Fingerprint(input),LogFile=Path.Combine(root,"AdvancedData","Logs","encode.log"),OutputFolder=Path.Combine(root,"AdvancedOutput"),FileName="resume"};Directory.CreateDirectory(Path.GetDirectoryName(j.LogFile));
  object logGate=new();Action<string> log=line=>{lock(logGate)File.AppendAllText(j.LogFile,line+"\n");};
  using(var cts=new CancellationTokenSource()) {
   bool cancelled=false;try{await Converter.Convert(j,s,cts.Token,log,()=>{if(j.Advanced?.Parts.Any(x=>x.Complete)==true)cts.Cancel();});}catch(OperationCanceledException){cancelled=true;}
   Check(cancelled&&j.Advanced.Parts[0].Complete,"cancellation leaves only verified complete chunks");
  }
  Check(j.Advanced.CacheFolder.StartsWith(EncodeCache.Root(root,s.EncodeCacheFolder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.Exists(j.Advanced.Parts[0].File),"custom cache destination retains verified video after cancellation");
  string corrupt=j.Advanced.Parts[0].File;File.AppendAllText(corrupt,"corruption");
  using(var cts=new CancellationTokenSource()) {bool cancelled=false;try{await Converter.Convert(j,s,cts.Token,log,()=>{if(j.Advanced?.Parts.Count(x=>x.Complete)>=2)cts.Cancel();});}catch(OperationCanceledException){cancelled=true;}Check(cancelled&&j.Advanced.ReusedParts==0&&j.Advanced.Parts.Take(2).All(x=>x.Complete),"corrupt cached interval is regenerated and cancellation preserves newly verified intervals");}
  await Converter.Convert(j,s,CancellationToken.None,log,()=>{});Check(j.Advanced.ReusedParts==2,"retry reuses verified chunks rather than restarting video");
  Check(!Directory.Exists(Path.GetDirectoryName(j.Advanced.CacheFolder))&&File.Exists(j.Output)&&j.CacheCleanupWarning.Length==0,"successful verified conversion deletes only its custom job cache and retains output");
  var probe=await Processes.Run(s.Ffprobe,new[]{"-v","error","-show_streams","-of","json",j.Output},CancellationToken.None);var d=Store.Json.Deserialize<Dictionary<string,object>>(probe.Output);var audios=MetadataReader.Items(d,"streams").Select(MetadataReader.Obj).Where(x=>MetadataReader.Text(x,"codec_type")=="audio").ToList();Check(audios.Count==2,"both audio streams survive final output");Check(MetadataReader.Text(MetadataReader.Obj(audios[1]["tags"]),"language")=="eng","secondary language survives mux");
  Check(MetadataReader.Int(audios[1],"channels")==6,"5.1 channel audio is retained without stereo downmix");
  Check(!Directory.Exists(j.Advanced.CacheFolder),"pipeline leaves no cache or lossless intermediate after success");
  p.Cadence=CadenceMode.MixedVfr;p.ResumeChunks=false;j.FileName="vfr";await Converter.Convert(j,s,CancellationToken.None,log,()=>{});Check(j.Advanced.Parts.All(x=>x.Mode=="film"),"every film interval classified as 23.976");
  Check(j.Advanced.Parts.All(x=>!File.Exists(x.File)),"non-resumable encoded chunks cleaned after merge");
  var decode=await Processes.Run(s.Ffmpeg,new[]{"-v","error","-xerror","-i",j.Output,"-f","null","-"},CancellationToken.None,null,120);Check(decode.ExitCode==0,"VFR concat output fully decodes");
  string native=Path.Combine(root,"native.ts"),progressive=Path.Combine(root,"p30.ts"),film=Path.Combine(root,"film12.ts"),mixed=Path.Combine(root,"mixed36.ts");
  foreach(bool interlaced in new[]{true,false}) {
   var g=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-y","-f","lavfi","-i","testsrc2=size=320x240:rate="+(interlaced?"60000/1001":"30000/1001"),"-f","lavfi","-i","sine=frequency=600:sample_rate=48000","-t","12","-vf",interlaced?"tinterlace=interleave_top":"null","-c:v","mpeg2video","-q:v","2","-c:a","aac",interlaced?native:progressive},CancellationToken.None,null,120);Check(g.ExitCode==0,"mixed cadence source section generated "+interlaced);
  }
  var extract=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-y","-i",input,"-t","12","-map","0:v:0","-map","0:a:0","-c","copy",film},CancellationToken.None);Check(extract.ExitCode==0,"film section for mixed fixture");
  string mixedList=Path.Combine(root,"mixed.txt");File.WriteAllText(mixedList,string.Join("\n",new[]{film,native,progressive}.Select(f=>"file '"+f.Replace('\\','/')+"'")));
  var mixedGen=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-y","-f","concat","-safe","0","-i",mixedList,"-c","copy",mixed},CancellationToken.None);Check(mixedGen.ExitCode==0,"real 24 film 60i 30p concatenated TS");
  j.Input=mixed;j.FileName="actual-mixed";await Converter.Convert(j,s,CancellationToken.None,log,()=>{});
  Check(j.Advanced.Parts.Any(x=>x.Mode=="film")&&j.Advanced.Parts.Any(x=>x.Mode=="video")&&j.Advanced.Parts.Any(x=>x.Mode=="progressive"),"mixed timeline distinguishes film native interlace and progressive sections");
  var stamps=await Processes.Run(s.Ffprobe,new[]{"-v","error","-select_streams","v:0","-show_frames","-show_entries","frame=best_effort_timestamp_time","-of","csv=p=0",j.Output},CancellationToken.None,null,120);
  var times=stamps.Output.Split('\n').Select(x=>x.Trim().TrimEnd(',')).Where(x=>double.TryParse(x,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out _)).Select(MetadataReader.Number).ToList();var deltas=times.Zip(times.Skip(1),(a,b)=>b-a).ToList();Check(deltas.All(x=>x>0)&&deltas.Count(x=>Math.Abs(x-1001.0/24000)<0.002)>30&&deltas.Count(x=>Math.Abs(x-1001.0/60000)<0.002)>30&&deltas.Count(x=>Math.Abs(x-1001.0/30000)<0.002)>30,"output timestamps actually contain 24 60 and 30 fps intervals in increasing order");
  decode=await Processes.Run(s.Ffmpeg,new[]{"-v","error","-xerror","-i",j.Output,"-f","null","-"},CancellationToken.None,null,120);Check(decode.ExitCode==0,"real mixed VFR output fully decodes");
  j.Input=input;
  File.WriteAllText(input+".edl","6 12 0\n");p.CmMode=CmMode.Cut;p.CmReview=false;j.FileName="multi-audio-cut";await Converter.Convert(j,s,CancellationToken.None,log,()=>{});
  Check(Math.Abs(Commercials.Duration(j)-30)<0.1&&File.Exists(j.Output),"advanced CM cut keeps both audio tracks on the retimed timeline");
  p.CmMode=CmMode.Off;
  if(p.Engine==Engine.VCEEncC){p.Cadence=CadenceMode.Preset;p.PipeGpu=false;j.FileName="gpu-intermediate";await Converter.Convert(j,s,CancellationToken.None,log,()=>{});Check(File.Exists(j.Output)&&!Directory.Exists(j.Advanced.CacheFolder),"GPU detailed encoding without pipe cleans intermediate and job cache after success");p.PipeGpu=true;
   var sourceCommand=new Command {Exe=s.Ffmpeg,Args=new(){"-hide_banner","-re","-f","lavfi","-i","testsrc2=size=320x240:rate=30","-t","30","-pix_fmt","yuv420p","-f","yuv4mpegpipe","pipe:1"}};
   var pipeMetadata=Store.Clone(m);pipeMetadata.AudioIndex=-1;var consumer=Commands.Hardware(s,p,pipeMetadata,"-",Path.Combine(root,"cancel-pipe.mkv"),true);consumer.Args.Remove("--avsw");consumer.Args.Insert(0,"--y4m");bool stopped=false;
   using(var cancelPipe=new CancellationTokenSource(1200)){try{await PipeEncoding.Run(sourceCommand,consumer,cancelPipe.Token,log);}catch(OperationCanceledException){stopped=true;}}Check(stopped,"cancellation while pipe is streaming terminates producer and GPU consumer");
  }
  var preview=await EncodingLab.Preview(input,p,s,10,Path.Combine(root,"Preview"),CancellationToken.None,log);Check(File.Exists(preview.Original)&&File.Exists(preview.Filtered),"comparison preview produces real source and filtered PNGs");
  p.Cadence=CadenceMode.Preset;p.PipeGpu=true;var trials=await EncodingLab.Trial(input,p,s,new[]{23,28},Path.Combine(root,"Trials"),CancellationToken.None,log);Check(trials.Count==2&&trials.All(x=>x.EstimatedMegabytes>0&&x.Speed>0&&x.Ssim>=0),"quality trials measure real encoded size speed and SSIM");
  var signal=await SignalDiagnostics.Analyze(input,m,s,CancellationToken.None,log);Check(signal.Packets>0&&signal.Fingerprint==Store.Fingerprint(input),"full TS packet and timestamp diagnostics completes");
  p.Engine=Engine.CPU;p.Codec=Codec.HEVC;p.Speed="ultrafast";p.ColorMatrix=VideoMatrix.Bt2020nc;p.InputColorMatrix=VideoMatrix.Bt709;p.BitDepth=10;p.ColorRange=VideoRange.Limited;j.FileName="color10";await Converter.Convert(j,s,CancellationToken.None,log,()=>{});
  var color=await Processes.Run(s.Ffprobe,new[]{"-v","error","-select_streams","v:0","-show_entries","stream=pix_fmt,color_space,color_primaries,color_transfer,color_range","-of","json",j.Output},CancellationToken.None);var cd=Store.Json.Deserialize<Dictionary<string,object>>(color.Output);var cv=MetadataReader.Obj(MetadataReader.Items(cd,"streams").First());Check(MetadataReader.Text(cv,"pix_fmt").Contains("10")&&MetadataReader.Text(cv,"color_primaries")=="bt2020","actual 10bit BT2020 conversion verified by probe");
  string offset=Path.Combine(root,"offset.ts");var offsetGen=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-y","-itsoffset","0.7","-f","lavfi","-i","testsrc2=size=320x240:rate=30","-f","lavfi","-i","aevalsrc=if(lt(t\\,0.7)\\,0.25*sin(2*PI*440*t)\\,0.25*sin(2*PI*880*t)):s=48000","-t","6","-c:v","mpeg2video","-q:v","2","-c:a","aac",offset},CancellationToken.None,null,60);Check(offsetGen.ExitCode==0,"source with audio before first video generated");
  j.Input=offset;j.FileName="offset-sync";p.ColorMatrix=p.InputColorMatrix=VideoMatrix.Preserve;p.BitDepth=8;p.Codec=Codec.H264;p.Cadence=CadenceMode.Preset;p.ResumeChunks=false;p.AutoInverseTelecine=false;await Converter.Convert(j,s,CancellationToken.None,log,()=>{});
  Check(j.Metadata.VideoStart-j.Metadata.AudioStart>0.5,"offset fixture contains a real audio-video start difference");
  string pcm=Path.Combine(root,"sync.f32");var samples=await Processes.Run(s.Ffmpeg,new[]{"-v","error","-y","-ss","0.05","-i",j.Output,"-t","0.2","-map","0:a:0","-ac","1","-ar","48000","-f","f32le",pcm},CancellationToken.None,null,60);Check(samples.ExitCode==0,"aligned audio samples extracted for signal check");
  byte[] audioBytes=File.ReadAllBytes(pcm);int crossings=0;float previous=0;for(int i=0;i+4<=audioBytes.Length;i+=4){float value=BitConverter.ToSingle(audioBytes,i);if(previous<0&&value>=0)crossings++;previous=value;}double frequency=crossings/(audioBytes.Length/4.0/48000);Check(Math.Abs(frequency-880)<60,"leading audio is correctly trimmed to the first video frame instead of retaining the earlier 440Hz segment");
  string gap=Path.Combine(root,"timestamp-gap.ts");var gapGen=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-y","-f","lavfi","-i","testsrc2=size=320x240:rate=30","-f","lavfi","-i","sine=frequency=880:sample_rate=48000","-t","6","-vf","setpts=PTS+if(gte(N\\,60)\\,0.4/TB\\,0)","-fps_mode","passthrough","-c:v","mpeg2video","-q:v","2","-c:a","aac",gap},CancellationToken.None,null,60);Check(gapGen.ExitCode==0,"timestamp-hole fixture generated without filling the missing frames");
  j.Input=gap;j.FileName="gap-aligned";p.Engine=args.Contains("--vce")?Engine.VCEEncC:Engine.CPU;p.Speed=p.Engine==Engine.CPU?"ultrafast":"balanced";p.PipeGpu=true;await Converter.Convert(j,s,CancellationToken.None,log,()=>{});var gapProbe=await Processes.Run(s.Ffprobe,new[]{"-v","error","-select_streams","v:0","-count_frames","-show_entries","stream=nb_read_frames,duration","-of","json",j.Output},CancellationToken.None);var gapData=Store.Json.Deserialize<Dictionary<string,object>>(gapProbe.Output);var gapVideo=MetadataReader.Obj(MetadataReader.Items(gapData,"streams").First());Check(Math.Abs(MetadataReader.Int(gapVideo,"nb_read_frames")-j.Metadata.Duration*30)<3,"timestamp-free encoding preserves duration by holding frames across a PTS hole");
  string seekGap=Path.Combine(root,"seek-gap.mkv");await AdvancedEncoder.EncodeVideo(j,s,new EncodePart {Start=2,End=5},seekGap,CancellationToken.None,log);await AdvancedEncoder.ValidateVideo(s,seekGap,3,CancellationToken.None,true);Check(true,"seek into a timestamp hole preserves leading delay rather than shortening the chunk");
 }
}
