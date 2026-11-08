using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static async Task AribRawTests(string root) {
  var settings=new Settings {Ffmpeg="ffmpeg",Ffprobe="ffprobe"};
  var meta=new Metadata {SubtitleIndex=2,SubtitleCodec="arib_caption",SubtitleStart=1.44,FormatStart=0,VideoStart=1.4,Duration=10,Start=new DateTime(2026,10,31)};
  string argsOf(Command c)=>string.Join(" ",c.Args);
  // Default stays converted-only: no raw input, ASS into MKV as before.
  var conv=new Job {Preset=new Preset {Container=Container.mkv},Metadata=meta};
  Check(!Subtitles.WantsRaw(conv)&&!Subtitles.RawArib(conv),"arib raw is off by default (converted subtitles)");
  var convCmd=Subtitles.Mux(settings,conv.Preset,meta,"video.mkv","caps.srt","out.mkv");
  Check(argsOf(convCmd).Contains("-c:s ass")&&!argsOf(convCmd).Contains("copy"),"converted mode muxes ASS text subtitles only");
  // AribRaw mode: raw TS input, stream copy of the caption stream, input seek aligning subtitle to video origin.
  var rawJob=new Job {Input="in.ts",Preset=new Preset {Container=Container.mkv,SubtitleMode=SubtitleMode.AribRaw},Metadata=meta};
  Check(Subtitles.WantsRaw(rawJob)&&Subtitles.RawArib(rawJob),"arib raw mode selected for MKV");
  string packed=await Subtitles.Pack(rawJob,settings,"video.mkv","arib-raw","out.mkv",CancellationToken.None,_=>{},c=>{
   string a=argsOf(c);
   Check(a.Contains("-ss 1.44")&&a.Contains("in.ts"),"arib raw mux seeks the TS input to the subtitle origin");
   Check(a.Contains("-map 1:2")&&a.Contains("-c:s:0 copy"),"arib raw mux copies the caption stream unconverted");
   Check(a.Contains("title=日本語（ARIB）"),"arib raw track is titled as ARIB");
   return Task.FromResult(new ProcessResult {ExitCode=0});
  });
  Check(packed=="arib-raw","arib raw pack returns the raw marker");
  // CM chapters shift the chapter input after video + raw subtitle.
  var chapJob=new Job {Input="in.ts",Preset=new Preset {Container=Container.mkv,SubtitleMode=SubtitleMode.AribRaw,CmMode=CmMode.Chapters},Metadata=meta,Commercials=new CmPlan {ChapterFile="ch.ffmeta"}};
  await Subtitles.Pack(chapJob,settings,"video.mkv","arib-raw","out.mkv",CancellationToken.None,_=>{},c=>{
   Check(argsOf(c).Contains("-map_chapters 2"),"arib raw with chapters maps chapters from input 2");
   return Task.FromResult(new ProcessResult {ExitCode=0});
  });
  // CM cut: the pre-cut subtitle-only TS is stream 0 of its input.
  var cutJob=new Job {Input="in.ts",Preset=new Preset {Container=Container.mkv,SubtitleMode=SubtitleMode.AribRaw,CmMode=CmMode.Cut},Metadata=meta,Commercials=new CmPlan()};
  string cutTs=Path.Combine(root,"arib-cut.ts");
  await Subtitles.Pack(cutJob,settings,"video.mkv",cutTs,"out.mkv",CancellationToken.None,_=>{},c=>{
   string a=argsOf(c);
   Check(a.Contains("-map 1:0")&&!a.Contains("-ss"),"arib raw CM-cut mux uses the cut TS stream 0 without seek");
   return Task.FromResult(new ProcessResult {ExitCode=0});
  });
  // Both mode: converted subtitle first (default), raw ARIB second.
  var bothJob=new Job {Input="in.ts",Preset=new Preset {Container=Container.mkv,SubtitleMode=SubtitleMode.Both},Metadata=meta};
  Check(Subtitles.WantsRaw(bothJob)&&!Subtitles.RawArib(bothJob),"both mode wants raw but still converts");
  await Subtitles.Pack(bothJob,settings,"video.mkv","caps.srt","out.mkv",CancellationToken.None,_=>{},c=>{
   string a=argsOf(c);
   Check(a.Contains("-map 1:0")&&a.Contains("-c:s:1 copy")&&a.Contains("-map 2:2"),"both mode muxes converted subtitle plus raw ARIB copy");
   Check(a.IndexOf("title=日本語\"")<a.IndexOf("title=日本語（ARIB）"),"both mode keeps converted subtitle as the first track");
   return Task.FromResult(new ProcessResult {ExitCode=0});
  });
  // MP4 never stores raw ARIB.
  var mp4Job=new Job {Preset=new Preset {Container=Container.mp4,SubtitleMode=SubtitleMode.AribRaw},Metadata=meta};
  Check(!Subtitles.WantsRaw(mp4Job),"arib raw does not apply to MP4 output");
  // Non-ARIB subtitle streams are never treated as raw.
  var dvbMeta=new Metadata {SubtitleIndex=1,SubtitleCodec="dvb_subtitle"};
  Check(!Subtitles.WantsRaw(new Job {Preset=new Preset {Container=Container.mkv,SubtitleMode=SubtitleMode.Both},Metadata=dvbMeta}),"non-ARIB subtitles stay on the converted path");
  // Preset persistence of the new mode.
  Check(Store.Clone(new Preset {SubtitleMode=SubtitleMode.Both}).SubtitleMode==SubtitleMode.Both,"subtitle mode survives preset clone roundtrip");
  Check(new Preset().SubtitleMode==SubtitleMode.Convert,"subtitle mode defaults to converted");
 }
}
