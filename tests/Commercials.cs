using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static async Task CmTests(string root) {
  Check(ToolInstaller.IsCmDependency("exe_files/chapter_exe.exe")&&!ToolInstaller.IsCmDependency("exe_files/AmatsukazeCLI.exe")&&!ToolInstaller.IsCmDependency("exe_files/x264.exe"),"CM downloader excludes Amatsukaze application and encoders");
  string index=Path.Combine(root,"test.lwi");File.WriteAllText(index,"<LSMASHWorksIndexVersion=0.0.2.0>\n<LibavReaderIndexFile=13>\n"+string.Join("\n",Enumerable.Range(0,4).Select(i=>"Index=0,Type=0,Codec=2,TimeBase=1/30,POS=0,PTS="+i+",DTS="+i+",EDI=0\nKey=1,Width=320,Height=180,Format=yuv420p"))+"\n</LibavReaderIndex>\n");
  var indexed=CmIndex.Read(index,0,4,out var iw,out var ih);Check(CmIndex.Align(indexed,new[]{2d/30,3d/30})==2&&iw.All(w=>w==320)&&ih.All(h=>h==180),"direct TS index maps leading decoder differences without video re-encoding");
  bool indexMismatch=false;try{CmIndex.Align(indexed,new[]{0d,2d/30});}catch(InvalidDataException){indexMismatch=true;}Check(indexMismatch,"interior decoder frame mismatch stops automatic CM application");
  File.AppendAllText(index,"ignored binary trailer");Check(CmIndex.Read(index,0,4,out _,out _).Length==4,"index reader stops before unrelated binary trailer");
  string tracks=Path.Combine(root,"tracks.lwi");File.WriteAllText(tracks,"<LibavReaderIndexFile=13>\n"+"Index=0,Type=0,Codec=2,TimeBase=1/90000,POS=188,PTS=90000,DTS=90000,EDI=0\nIndex=0,Type=0,Codec=2,TimeBase=1/90000,POS=376,PTS=93000,DTS=93000,EDI=0\nIndex=1,Type=1,Codec=86018,TimeBase=1/90000,POS=564,PTS=90000,DTS=90000,EDI=0\nIndex=1,Type=1,Codec=86018,TimeBase=1/90000,POS=752,PTS=91920,DTS=91920,EDI=0\n</LibavReaderIndex>\n");
  Check(CmIndex.MapStream(tracks,0,new[]{(188L,1d),(376L,93000d/90000)})==0&&CmIndex.MapStream(tracks,1,new[]{(564L,1d),(752L,91920d/90000)})==1,"native track mapping uses packet positions and timestamps rather than FFmpeg stream numbers");
  bool wrongTrack=false;try{CmIndex.MapStream(tracks,0,new[]{(564L,1d),(752L,91920d/90000)});}catch(InvalidDataException){wrongTrack=true;}Check(wrongTrack,"audio packet positions cannot select a native video track");
  bool wrongPacketTime=false;try{CmIndex.MapStream(tracks,0,new[]{(188L,2d),(376L,3d)});}catch(InvalidDataException){wrongPacketTime=true;}Check(wrongPacketTime,"packet position matches require matching timestamps");
  var cuts=new[]{new TimeRange {Start=5,End=10},new TimeRange {Start=15,End=18}};
  var plan=Commercials.Build(cuts,20,30,"test");Check(plan.Segments.Count==5&&plan.OutputDuration==12,"CM timeline covers program and commercials");
  Check(Commercials.HardwareTrim(plan.Keep,30)=="0:149,300:449,540:599","GPU trim uses inclusive last frames");
  Check(Commercials.Build(new[]{new TimeRange {Start=5,End=10},new TimeRange {Start=9,End=12}},20,30,"x").Segments.Count==3,"overlapping CM candidates merged");
  Reject(()=>Commercials.Build(new[]{new TimeRange {Start=-1,End=2}},20,30,"x"),"negative CM time rejected");Reject(()=>Commercials.Build(new[]{new TimeRange {Start=0,End=25}},20,30,"x"),"CM time outside recording rejected");
  Reject(()=>Commercials.Build(new[]{new TimeRange {Start=double.NaN,End=10}},20,30,"x"),"nonfinite CM time rejected");
  var m=new Metadata {Duration=20,Fps=30,Width=320,Height=180,Sar=1,VideoIndex=0,AudioIndex=1,Start=new DateTime(2026,10,31)};string edl=Path.Combine(root,"record.edl"),avs=Path.Combine(root,"record.trim.avs");
  File.WriteAllText(edl,"5 10 0\n15 18 3\n1 2 2\n");File.WriteAllText(avs,"Trim(0,149) ++ Trim(300,449) ++ Trim(540,599)");
  Check(Commercials.Import(edl,m,new Preset(),"x").OutputDuration==12,"EDL commercial intervals imported");Check(Commercials.Import(avs,m,new Preset(),"x").OutputDuration==12,"join_logo_scp style Trim retained ranges imported");
  File.WriteAllText(avs,"Trim(0,599)");var imprecise=Store.Clone(m);imprecise.Duration=20+1e-12;Check(Commercials.Import(avs,imprecise,new Preset(),"x").Segments.All(x=>!x.IsCommercial),"floating point tail does not create a sub-frame CM interval");
  File.WriteAllText(avs,"Import(\"danger.avs\")\nTrim(0,149)");Reject(()=>Commercials.Import(avs,m,new Preset(),"x"),"AVS source code never executed or accepted as trim expression");
  string srt="1\n00:00:00,000 --> 00:00:03,000\n本編\n\n2\n00:00:06,000 --> 00:00:07,000\nCM内\n\n3\n00:00:09,000 --> 00:00:11,000\n境界\n\n4\n00:00:14,000 --> 00:00:19,000\n両境界\n\n";
  var retimed=Commercials.RetimeSrt(srt,plan.Keep);Check(!retimed.Contains("CM内")&&retimed.Contains("00:00:05,000 --> 00:00:06,000"),"subtitles in removed CM disappear and later cues move forward");Check(retimed.Contains("00:00:09,000 --> 00:00:10,000")&&retimed.Contains("00:00:10,000 --> 00:00:11,000"),"subtitle crossing a removed interval splits correctly");
  Check(Commercials.Chapters(plan,CmMode.Chapters).Contains("CM候補")&&!Commercials.Chapters(plan,CmMode.Cut).Contains("CM候補"),"chapter mode labels CM while cut mode retains program chapters");
  var hardware=Commands.Hardware(new Settings(),new Preset {Engine=Engine.VCEEncC},m,"input.ts","out.mp4",false,plan.Keep);Check(hardware.Args.Contains("--trim")&&hardware.Args.Contains("0:149,300:449,540:599"),"AMD command receives CM trim ranges");
  Reject(()=>Commands.Validate(new Preset {CmMode=CmMode.Cut,CopyAudio=true}),"CM cut rejects audio copy rather than misaligning samples");
  string input=Path.Combine(root,"cm-source.ts");File.WriteAllText(input,"fixture");string fingerprint=Store.Fingerprint(input);var job=new Job {Input=input,Metadata=m,Preset=new Preset {CmMode=CmMode.Chapters,CmReview=true}};
  bool review=false;try{await Commercials.Prepare(job,new Settings(),root,CancellationToken.None,_=>{},()=>{});}catch(CmReviewException){review=true;}Check(review&&job.Commercials!=null&&!job.Commercials.Approved,"no CM sidecar waits for manual review without deleting content");
  using(var q=new QueueManager(Path.Combine(root,"cm-queue"))){q.Paused=true;var j=q.Enqueue(input);j.Metadata=m;j.State=State.NeedsCmReview;q.ApproveCm(new QueueManager.Request {JobId=j.Id,Fingerprint=fingerprint,CmMode=CmMode.Cut,CmCuts=cuts.ToList()});Check(j.State==State.Waiting&&j.Commercials.Approved&&j.Commercials.Reviewed,"review confirmation resumes job with validated intervals");}
  job.Preset.CmSource=CmSource.Comskip;job.Commercials=null;bool rejected=false;
  try{await Commercials.Prepare(job,new Settings(),root,CancellationToken.None,_=>{},()=>{});}catch(InvalidDataException){rejected=true;}
  Check(rejected&&job.Commercials==null,"retired CM backend cannot run or silently approve content");
  CmAnalysis.ValidateTimeline(Enumerable.Range(0,600).Select(i=>i/30d).ToArray(),600,m);
  bool badTimeline=false;try{CmAnalysis.ValidateTimeline(new[]{0d,1d},2,m);}catch(InvalidDataException){badTimeline=true;}Check(badTimeline,"changed CM frame timeline stops automatic cut");
  bool traversal=false;try{ToolInstaller.SafeEntry(root,"../escape.exe");}catch(InvalidDataException){traversal=true;}Check(traversal,"download archive traversal rejected");

 }
 static async Task CmLive(string root,string[] args) {
  string ffmpeg=Store.FindTool("ffmpeg.exe"),probe=Store.FindTool("ffprobe.exe"),input=Path.Combine(root,"CMテスト & 日本語.ts");
  string colors="color=red:s=320x180:r=30:d=5[a];color=black:s=320x180:r=30:d=5[b];color=blue:s=320x180:r=30:d=5[c];color=black:s=320x180:r=30:d=3[d];color=yellow:s=320x180:r=30:d=2[e];[a][b][c][d][e]concat=n=5:v=1:a=0";
  var generate=await Processes.Run(ffmpeg,new[]{"-hide_banner","-y","-f","lavfi","-i",colors,"-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-t","20","-c:v","mpeg2video","-b:v","2000k","-c:a","aac","-f","mpegts",input},CancellationToken.None,null,60);Check(generate.ExitCode==0,"CM test MPEG-TS fixture created");
  File.WriteAllText(input+".edl","5 10 0\n15 18 0\n");File.WriteAllText(input+".program.txt","2026/10/31(土) 13:00～13:01\r\nテスト局\r\nCMテスト番組\r\n\r\n説明\r\n",Encoding.GetEncoding(932));
  string vce=args.Contains("--vce")?Option(args,"--vce"):null;
  foreach(var mode in new[]{CmMode.Cut,CmMode.Chapters}) {
   var preset=new Preset {Name="CM test",CmMode=mode,CmReview=false,Width=320,Height=180,Speed=vce==null?"ultrafast":"balanced",Engine=vce==null?Engine.CPU:Engine.VCEEncC,Threads=2};
   var settings=new Settings {Ffmpeg=ffmpeg,Ffprobe=probe,Vceenc=vce??""};var j=new Job {Input=input,Preset=preset,OutputFolder=Path.Combine(root,"cm-output"),FileName="{OriginalName}_"+mode,LogFile=Path.Combine(root,"Data","Logs",mode+".log")};
   await Converter.Convert(j,settings,CancellationToken.None,_=>{},()=>{});Check(File.Exists(j.Output)&&File.Exists(j.Output+".cm.csv"),mode+" creates video and CM timeline CSV");
   var result=await Processes.Run(probe,new[]{"-v","error","-show_streams","-show_format","-show_chapters","-of","json",j.Output},CancellationToken.None,null,30);var data=Store.Json.Deserialize<Dictionary<string,object>>(result.Output);double duration=MetadataReader.Number(MetadataReader.Text(MetadataReader.Obj(data["format"]),"duration"));
   Check(Math.Abs(duration-(mode==CmMode.Cut?12:20))<0.2,mode+" output duration matches timeline");Check(MetadataReader.Items(data,"chapters").Count()==(mode==CmMode.Cut?3:5),mode+" embeds all expected program and CM chapters");
   Check(MetadataReader.Items(data,"chapters").Select(MetadataReader.Obj).All(x=>MetadataReader.Text(MetadataReader.Obj(x["tags"]),"title").Length>0),mode+" retains readable chapter titles");
   var streams=MetadataReader.Items(data,"streams").Select(MetadataReader.Obj).ToList();Check(streams.Any(x=>MetadataReader.Text(x,"codec_type")=="audio"),mode+" retains audio");
   Check(Math.Abs(MetadataReader.Number(MetadataReader.Text(streams.First(x=>MetadataReader.Text(x,"codec_type")=="audio"),"duration"))-duration)<0.15,mode+" audio duration stays synchronized");
   var decode=await Processes.Run(ffmpeg,new[]{"-hide_banner","-v","error","-xerror","-i",j.Output,"-map","0:v","-map","0:a","-f","null","-"},CancellationToken.None,null,60);Check(decode.ExitCode==0&&string.IsNullOrWhiteSpace(decode.Error),mode+" full output decodes without errors");
   if(mode==CmMode.Cut) {
    string sub=Path.Combine(root,"cut-check.srt"),packed=Path.Combine(root,"cut-with-subtitles.mp4");File.WriteAllText(sub,Commercials.RetimeSrt("1\n00:00:11,000 --> 00:00:12,000\n字幕同期テスト\n\n",j.Commercials.Keep));
    await Subtitles.Pack(j,settings,j.Output,sub,packed,CancellationToken.None,_=>{});
    var extract=await Processes.Run(ffmpeg,new[]{"-v","error","-i",packed,"-map","0:s:0","-f","srt","-"},CancellationToken.None,null,30);Check(extract.ExitCode==0&&extract.Output.Contains("00:00:06,000 --> 00:00:07,000")&&extract.Output.Contains("字幕同期テスト"),"actual mux retains retimed subtitle after CM cut");
   }
   if(mode==CmMode.Cut)foreach(var sample in new[]{(5.2,"blue"),(10.2,"yellow")}) {
    string bmp=Path.Combine(root,"sample-"+sample.Item2+".bmp");var shot=await Processes.Run(ffmpeg,new[]{"-hide_banner","-v","error","-y","-ss",Commercials.N(sample.Item1),"-i",j.Output,"-frames:v","1","-vf","scale=2:2","-pix_fmt","bgr24",bmp},CancellationToken.None,null,30);Check(shot.ExitCode==0,"cut boundary frame extracted "+sample.Item2);
    var bytes=File.ReadAllBytes(bmp);int offset=BitConverter.ToInt32(bytes,10),b=bytes[offset],g=bytes[offset+1],r=bytes[offset+2];Check(sample.Item2=="blue"?b>150&&r<60:r>150&&g>150&&b<60,"cut removes actual black CM frames before "+sample.Item2);
   }
  }
  Check(File.Exists(input),"CM processing keeps original TS");Console.WriteLine("CM OUTPUT ROOT "+root);
 }
}
