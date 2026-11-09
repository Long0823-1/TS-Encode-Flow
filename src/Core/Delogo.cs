using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class DelogoReceipt {public string Source="";public string Output="";}
 public sealed class LogoSegment {public int Start,End,FadeIn,FadeOut;}
 // 透過ロゴ消し: AviSynth + DelogoHD で LGD からロゴを逆算除去し、可逆の中間映像を作る。
 // AviSynth / L-SMASH は CM解析コンポーネント（rigaya/Amatsukaze 配布）から借り、
 // 実行ファイルとDLLは logoframe と同じ流儀で専用ランタイムフォルダーへ集めて使う。
 // CMカット時は保持区間を AVS の Trim で切り、中間映像自体をカット済みにする。
 public static class Delogo {
  static string Avs(string path) {if(path.IndexOfAny(new[]{'"','\r','\n'})>=0)throw new ArgumentException("AVSに使用できないパスです。");return "\""+Path.GetFullPath(path).Replace('\\','/')+"\"";}
  public static string ResolveLogo(string data,Job job,Settings settings) {
   string logo=!string.IsNullOrWhiteSpace(settings.CmLogo)?settings.CmLogo:StationLogos.Find(data,job);
   if(string.IsNullOrWhiteSpace(logo)||!File.Exists(logo))throw new InvalidDataException("透過ロゴ消しには登録済み局ロゴ（LGD）が必要です。ロゴ管理で局ロゴを登録するか、設定でロゴLGDを指定してください。");
   LogoCandidates.Read(logo,job.Metadata);return logo;
  }
  public static Metadata VideoOnly(Metadata source) {
   var m=Store.Clone(source);m.VideoIndex=0;m.AudioIndex=-1;return m;
  }
  // avs2pipemod の y4m モードと SAR 指定。フィールド順は ffprobe 由来の表記に合わせる。
  public static string PipeMode(Metadata m) {
   string mode=m.FieldOrder=="bb"||m.FieldOrder=="bt"?"-y4mb":m.FieldOrder=="progressive"?"-y4mp":"-y4mt";
   return mode+"="+Sar(m.Sar);
  }
  static string Sar(double sar) {
   if(!double.IsFinite(sar)||sar<=0)return "1:1";
   for(int den=1;den<=1000;den++){int num=(int)Math.Round(sar*den);if(num>0&&Math.Abs(num/(double)den-sar)<0.0005)return num.ToString(CultureInfo.InvariantCulture)+":"+den.ToString(CultureInfo.InvariantCulture);}
   return "1:1";
  }
  static string N(int n)=>n.ToString(CultureInfo.InvariantCulture);
  static int Gcd(int a,int b){a=Math.Abs(a);b=Math.Abs(b);while(b!=0){int t=a%b;a=b;b=t;}return a==0?1:a;}
  // segments: ロゴ表示区間（フレーム番号）。null/空なら全編に一様適用。
  // keep: CMカット保持区間（フレーム番号・終了含む）。nullならカットなし。
  public static string Script(string lsmash,string delogo,string source,int streamIndex,string logo,double fps,IReadOnlyList<LogoSegment> segments=null,IReadOnlyList<(int Start,int End)> keep=null) {
   var b=new StringBuilder();
   b.Append("ClearAutoloadDirs()\nLoadPlugin("+Avs(lsmash)+")\nLoadPlugin("+Avs(delogo)+")\n");
   b.Append("v=LWLibavVideoSource("+Avs(source)+(streamIndex>=0?",stream_index="+N(streamIndex):"")+",threads=4,repeat=false)\n");
   if(segments==null||segments.Count==0)b.Append("v=v.DelogoHD(logofile="+Avs(logo)+")\n");
   else foreach(var seg in segments) {
    b.Append("v=v.DelogoHD(logofile="+Avs(logo)+",start="+N(seg.Start)+",end="+N(seg.End));
    if(seg.FadeIn>0)b.Append(",fadein="+N(seg.FadeIn));
    if(seg.FadeOut>0)b.Append(",fadeout="+N(seg.FadeOut));
    b.Append(")\n");
   }
   if(keep!=null&&keep.Count>0) {
    b.Append("v="+string.Join("++",keep.Select(r=>"v.Trim("+N(r.Start)+","+N(r.End)+")"))+"\n");
   }
   // Reduce the fps rational (30000/1001, not 29970030/1000000): consumers of
   // the y4m header (ffmpeg's demuxer) reject oversized header lines.
   int fpsNum=(int)Math.Round(fps*1000),fpsDen=1000,g=Gcd(fpsNum,fpsDen);fpsNum/=g;fpsDen/=g;
   b.Append("v.AssumeFPS("+N(fpsNum)+","+N(fpsDen)+")\n");
   return b.ToString();
  }
  // AVS Trim で中間映像を事前にCMカットするか。詳細パイプライン使用時は null:
  // AdvancedEncoder.Parts は元タイムラインの開始位置で入力をシークするため、
  // 中間映像は全長のまま渡し、カットは区間分割に任せる。
  public static List<(int Start,int End)> TrimFrames(Job job) {
   var keep=Commercials.Keep(job);
   if(keep==null||AdvancedOptions.Enabled(job.Preset))return null;
   return FrameRanges(keep,job.Metadata.Fps);
  }
  // CM保持区間（秒）をフレーム区間へ。区間はCM側でフレーム境界に丸め済み。
  public static List<(int Start,int End)> FrameRanges(IReadOnlyList<TimeRange> keep,double fps) {
   var result=new List<(int,int)>();
   if(keep==null)return result;
   foreach(var r in keep) {
    int start=Math.Max(0,(int)Math.Round(r.Start*fps)),end=(int)Math.Round(r.End*fps)-1;
    if(end>=start)result.Add((start,end));
   }
   return result;
  }
  // logoframe の -oa 出力（S/E 行のペア）を表示区間へ変換する。書式は third_party/CmLogo の LogoWriteFind_Sub に準拠。
  // fade_rise / fade_fall は 0=フェードなし、>0=その秒数×FPS が DelogoHD の fadein / fadeout フレーム数。
  public static List<LogoSegment> ParseLogoSegments(string text,double fps) {
   var result=new List<LogoSegment>();LogoSegment current=null;
   foreach(string raw in (text??"").Split('\n')) {
    var m=Regex.Match(raw.Trim(),@"^(\d+)\s+([SE])\s+(\d+)\s+(ALL|TOP|BTM)\s+(-?\d+)\s+(-?\d+)$");
    if(!m.Success)continue;
    int frame=int.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture),fade=int.Parse(m.Groups[3].Value,CultureInfo.InvariantCulture);
    int fadeFrames=Math.Max(0,(int)Math.Round(fade*fps));
    if(m.Groups[2].Value=="S") {current=new LogoSegment {Start=frame,FadeIn=fadeFrames};result.Add(current);}
    else if(current!=null) {current.End=frame;current.FadeOut=fadeFrames;current=null;}
   }
   result.RemoveAll(s=>s.End<s.Start);
   result.Sort((a,b)=>a.Start.CompareTo(b.Start));
   // 隣接・重複区間はフェード指定なしで単純化してつなぐ（DelogoHD の範囲適用は重ね掛けできない）。
   var merged=new List<LogoSegment>();
   foreach(var s in result) {
    if(merged.Count>0&&s.Start<=merged[merged.Count-1].End+1) {var last=merged[merged.Count-1];last.End=Math.Max(last.End,s.End);last.FadeIn=0;last.FadeOut=0;}
    else merged.Add(s);
   }
   return merged;
  }
  static async Task<(long Position,double Time)[]> Packets(Settings settings,string input,int stream,CancellationToken token) {
   var probe=await Processes.Run(settings.Ffprobe,new[]{"-v","error","-read_intervals","%+10","-select_streams",stream.ToString(CultureInfo.InvariantCulture),"-show_packets","-show_entries","packet=pos,pts_time","-of","json",input},token);
   if(probe.ExitCode!=0)throw new InvalidDataException("ロゴ消しのトラック照合用パケットを取得できません。");
   var json=Store.Json.Deserialize<Dictionary<string,object>>(probe.Output);
   return MetadataReader.Items(json,"packets").Select(MetadataReader.Obj).Select(p=>(long.TryParse(MetadataReader.Text(p,"pos"),out var pos)?pos:-1,double.TryParse(MetadataReader.Text(p,"pts_time"),NumberStyles.Float,CultureInfo.InvariantCulture,out var time)?time:double.NaN)).ToArray();
  }
  // logoframe を実行してロゴ表示区間を得る。ツール不足・検出失敗時は null（全編適用へフォールバック）。
  static async Task<List<LogoSegment>> ScanSegments(Job job,Settings settings,string data,string scanScript,string runtimeLsmash,string folder,CancellationToken token,Action<string> log) {
   string logoExe=settings.LogoFrame;
   string bundled=Path.Combine(AppContext.BaseDirectory,"Tools","CmLogo","logoframe.exe"),managed=Path.GetFullPath(Path.Combine(data,"Tools","logoframe"))+Path.DirectorySeparatorChar;
   if(File.Exists(bundled)&&(string.IsNullOrWhiteSpace(logoExe)||Path.GetFullPath(logoExe).StartsWith(managed,StringComparison.OrdinalIgnoreCase)))logoExe=bundled;
   if(!File.Exists(logoExe)) {log("logoframeがないためロゴ区間検出を省略し、全編にロゴ消しを適用します。");return null;}
   string bin=Path.GetDirectoryName(settings.ChapterExe);
   string runtime=Path.GetDirectoryName(logoExe);foreach(string dll in new[]{"AviSynth.dll","vcruntime140.dll","msvcp140.dll"}){string source=Path.Combine(bin,dll);if(File.Exists(source)&&!File.Exists(Path.Combine(runtime,dll)))File.Copy(source,Path.Combine(runtime,dll));}
   string logoOutput=Path.Combine(folder,"delogo-logo.txt");
   string logoPath=ResolveLogoPath(job,settings,data);
   if(string.IsNullOrWhiteSpace(logoPath)||!File.Exists(logoPath))return null;
   try {
    var scan=await Processes.Run(logoExe,new[]{scanScript,"-oa",logoOutput,"-logo",logoPath},token,log);
    if(scan.ExitCode!=0||!File.Exists(logoOutput)) {log("ロゴ区間検出に失敗したため、全編にロゴ消しを適用します。");return null;}
    var segments=ParseLogoSegments(File.ReadAllText(logoOutput),job.Metadata.Fps);
    if(segments.Count==0) {log("ロゴ表示区間が検出されなかったため、全編にロゴ消しを適用します。");return null;}
    log("ロゴ表示区間を検出："+segments.Count+"区間（フェード追随あり）");return segments;
   }catch(OperationCanceledException){throw;}catch(Exception e)when(e is IOException||e is InvalidDataException||e is ArgumentException) {log("ロゴ区間検出を省略します："+e.Message);return null;}
  }
  static string ResolveLogoPath(Job job,Settings settings,string data)=>!string.IsNullOrWhiteSpace(settings.CmLogo)?settings.CmLogo:StationLogos.Find(data,job);
  public static async Task<string> Prepare(Job job,Settings settings,string data,CancellationToken token,Action<string> log,Action changed) {
   string logo=ResolveLogo(data,job,settings);
   string plugin=settings.DelogoPlugin,host=settings.Avs2PipeMod;
   if(!File.Exists(plugin))throw new FileNotFoundException("DelogoHDプラグインがありません。ツール設定で「ロゴ消し · DelogoHD」をダウンロードして適用してください。",plugin);
   if(!File.Exists(host))throw new FileNotFoundException("avs2pipemodがありません。ツール設定で「ロゴ消し · avs2pipemod」をダウンロードして適用してください。",host);
   string bin=string.IsNullOrWhiteSpace(settings.ChapterExe)?"":Path.GetDirectoryName(settings.ChapterExe);
   string lsmash=bin==""?"":Path.Combine(bin,"plugins64","LSMASHSource.dll"),avisynth=bin==""?"":Path.Combine(bin,"AviSynth.dll");
   if(lsmash==""||!File.Exists(lsmash)||!File.Exists(avisynth))throw new FileNotFoundException("CM解析用のAviSynth／L-SMASHがありません。ツール設定でCM解析コンポーネントをダウンロードして適用してください。",lsmash);
   string sourceFingerprint=Store.Fingerprint(job.MediaInput);
   var trimFrames=TrimFrames(job);
   var keepRanges=Commercials.Keep(job);
   string folder=EncodeCache.WorkFolder(data,settings,job);
   string output=Path.Combine(folder,"delogo-video.mkv"),receiptFile=output+".json";
   string key=sourceFingerprint+"|delogo-v2|"+Store.Fingerprint(logo)+"|"+Store.Fingerprint(plugin)+"|"+Store.Fingerprint(host)+"|"+Store.Fingerprint(lsmash)+"|"+Store.Json.Serialize(trimFrames);
   var receipt=Store.Load(receiptFile,()=>new DelogoReceipt());
   double expectedDuration=trimFrames!=null?keepRanges.Sum(r=>r.End-r.Start):job.Metadata.Duration;
   if(File.Exists(output)&&receipt.Source==key&&receipt.Output==Store.Fingerprint(output)) {
    try{await ValidateIntermediate(settings,output,job.Metadata,expectedDuration,token);log("ロゴ消し済み映像をキャッシュから再利用します。");return output;}catch(InvalidDataException){log("ロゴ消し済み映像のキャッシュが壊れているため作り直します。");}
   }
   job.Message="透過ロゴを除去中（AviSynth + DelogoHD）";changed();
   // Private AviSynth runtime: host exe + runtime + plugins in one folder, like logoframe.
   string runtime=Path.Combine(folder,"delogo-runtime");Directory.CreateDirectory(runtime);
   string hostExe=Path.Combine(runtime,Path.GetFileName(host));File.Copy(host,hostExe,true);
   foreach(string dll in new[]{"AviSynth.dll","vcruntime140.dll","msvcp140.dll"}){string source=Path.Combine(bin,dll);if(File.Exists(source))File.Copy(source,Path.Combine(runtime,dll),true);}
   string runtimeLsmash=Path.Combine(runtime,"LSMASHSource.dll"),runtimePlugin=Path.Combine(runtime,"DelogoHD.dll");
   File.Copy(lsmash,runtimeLsmash,true);File.Copy(plugin,runtimePlugin,true);
   Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
   var encoding=Encoding.GetEncoding(932,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);
   // L-SMASH writes its .lwi next to the source; build it once, keep a private copy, restore any pre-existing sidecar.
   string privateIndex=Path.Combine(folder,"delogo-index.lwi");
   bool rebuildIndex=!File.Exists(privateIndex)||Store.Load(receiptFile,()=>new DelogoReceipt()).Source!=key;
   if(rebuildIndex) {
    string probeScript=Path.Combine(folder,"delogo-probe.avs");
    File.WriteAllText(probeScript,"ClearAutoloadDirs()\nLoadPlugin("+Avs(runtimeLsmash)+")\nLWLibavVideoSource("+Avs(job.MediaInput)+",threads=4,repeat=false)\n",encoding);
    string sharedIndex=Path.GetFullPath(job.MediaInput)+".lwi",backup=Path.Combine(folder,"previous-delogo-index.lwi");
    // Share the canonical CM index when it exists (same source + same L-SMASH):
    // the delogo source is MediaInput, exactly what CM analysis indexed, and an
    // .lwi is a pure function of (source bytes, index format) — L-SMASH ignores
    // stream_index when building. Copying it avoids a second full indexing pass
    // whose -info run fails on some real recordings (empty stderr, exit != 0).
    string canonical=Path.Combine(data,"Commercials","IndexCache",CmIndex.Key(job.MediaInput)+".lwi"),canonicalReceipt=canonical+".identity";
    string canonicalIdentity=Store.Fingerprint(job.MediaInput)+"|"+Store.Fingerprint(lsmash);
    bool reused=false;
    if(File.Exists(canonical)&&File.Exists(canonicalReceipt)&&File.ReadAllText(canonicalReceipt)==canonicalIdentity) {
     try{File.Copy(canonical,privateIndex,true);reused=true;log("CM解析のインデックスをロゴ消しにも再利用します。");}catch(IOException){reused=false;}
    }
    if(!reused) {
    // Serialize the sidecar build against CM analysis (same input = same .lwi path).
    using var lease=await CmIndex.Lock(job.MediaInput,token);
    bool hadIndex=File.Exists(sharedIndex);if(hadIndex)File.Move(sharedIndex,backup);
    try {
     var info=await Processes.Run(hostExe,new[]{"-info",probeScript},token,log);
     if(info.ExitCode!=0) {
      string detail=(string.IsNullOrWhiteSpace(info.Error)?info.Output:info.Error);
      string tail=string.IsNullOrWhiteSpace(detail)?"(出力なし)":Converter.Tail(detail.Trim());
      throw new InvalidDataException("ロゴ消し用インデックスを作成できません（avs2pipemod 終了コード "+info.ExitCode+"）："+tail);
     }
     if(!File.Exists(sharedIndex))throw new InvalidDataException("ロゴ消し用インデックスを作成できません。入力フォルダーの書き込み権限を確認してください。");
     File.Copy(sharedIndex,privateIndex,true);
    }finally {
     if(File.Exists(sharedIndex))File.Delete(sharedIndex);
     if(hadIndex&&File.Exists(backup))File.Move(backup,sharedIndex);
    }
    }
   }
   // Map the selected FFmpeg video stream to the L-SMASH index stream (same method as CM analysis).
   int nativeVideo=CmIndex.MapStream(privateIndex,0,await Packets(settings,job.MediaInput,job.Metadata.VideoIndex,token));
   // Logo display segments for fade-aware erasure (best effort; falls back to full-range).
   List<LogoSegment> segments=null;
   if(!rebuildIndex&&File.Exists(Path.Combine(folder,"delogo-logo.txt")))segments=ParseLogoSegments(File.ReadAllText(Path.Combine(folder,"delogo-logo.txt")),job.Metadata.Fps);
   if(segments==null||segments.Count==0) {
    string scanScript=Path.Combine(folder,"delogo-scan.avs");
    File.WriteAllText(scanScript,"ClearAutoloadDirs()\nLoadPlugin("+Avs(runtimeLsmash)+")\nLWLibavVideoSource("+Avs(privateIndex)+",stream_index="+N(nativeVideo)+",threads=4,repeat=false).AssumeFPS("+N((int)Math.Round(job.Metadata.Fps*1000000))+",1000000).ConvertToYV12()\n",encoding);
    segments=await ScanSegments(job,settings,data,scanScript,runtimeLsmash,folder,token,log);
   }
   string script=Path.Combine(folder,"delogo.avs");
   File.WriteAllText(script,Script(runtimeLsmash,runtimePlugin,privateIndex,nativeVideo,logo,job.Metadata.Fps,segments,trimFrames),encoding);
   string pending=output+".partial.mkv";
   try {
    // y4m pipe, the transport proven by logoframe/CM on real material.
    // (A rawvideo attempt crashed avs2pipemod before frame 0 on a real
    // interlaced recording — its raw path lacks the y4m field correction —
    // while the plain-LWLibav scan script, no ConvertToYV12, works in y4m.)
    var producer=new Command {Exe=hostExe,Args=new List<string>{PipeMode(job.Metadata),"-dll="+Path.Combine(runtime,"AviSynth.dll"),script}};
    var consumer=new Command {Exe=settings.Ffmpeg,Args=new List<string>{"-hide_banner","-nostdin","-y","-f","yuv4mpegpipe","-i","-","-an","-c:v","ffv1","-level","3",pending}};
    log(producer+" | "+consumer);
    await PipeEncoding.Run(producer,consumer,token,log,"avs2pipemod","FFmpeg");
    token.ThrowIfCancellationRequested();
    if(Store.Fingerprint(job.MediaInput)!=sourceFingerprint)throw new IOException("ロゴ消し中に入力ファイルが更新されました。");
    await ValidateIntermediate(settings,pending,job.Metadata,expectedDuration,token);
    File.Move(pending,output,true);
   }finally {if(File.Exists(pending))File.Delete(pending);}
   Store.Save(receiptFile,new DelogoReceipt {Source=key,Output=Store.Fingerprint(output)});
   log("ロゴ消し済み映像を作成しました："+output);return output;
  }
  static async Task ValidateIntermediate(Settings settings,string file,Metadata m,double expectedDuration,CancellationToken token) {
   await AdvancedEncoder.ValidateVideo(settings,file,expectedDuration,token);
   var probe=await Processes.Run(settings.Ffprobe,new[]{"-v","error","-show_streams","-of","json",file},token,null,60);
   if(probe.ExitCode!=0)throw new InvalidDataException("ロゴ消し済み映像を読み取れません。");
   var data=Store.Json.Deserialize<Dictionary<string,object>>(probe.Output);
   var video=MetadataReader.Items(data,"streams").Select(MetadataReader.Obj).FirstOrDefault(x=>MetadataReader.Text(x,"codec_type")=="video");
   if(video==null||MetadataReader.Int(video,"width")!=m.Width||MetadataReader.Int(video,"height")!=m.Height)throw new InvalidDataException("ロゴ消し済み映像の解像度が元TSと一致しません。");
  }
 }
}
