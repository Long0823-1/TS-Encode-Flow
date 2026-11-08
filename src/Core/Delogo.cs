using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class DelogoReceipt {public string Source="";public string Output="";}
 // 透過ロゴ消し: AviSynth + DelogoHD で LGD からロゴを逆算除去し、可逆の中間映像を作る。
 // AviSynth / L-SMASH は CM解析コンポーネント（rigaya/Amatsukaze 配布）から借り、
 // 実行ファイルとDLLは logoframe と同じ流儀で専用ランタイムフォルダーへ集めて使う。
 public static class Delogo {
  static string Avs(string path) {if(path.IndexOfAny(new[]{'"','\r','\n'})>=0)throw new ArgumentException("AVSに使用できないパスです。");return "\""+Path.GetFullPath(path).Replace('\\','/')+"\"";}
  public static void ValidateCombination(Preset p) {
   if(!p.RemoveLogo)return;
   if(p.CmMode!=CmMode.Off)throw new ArgumentException("透過ロゴ消しはCM処理（チャプター・カット）との併用に未対応です。CM処理をオフにしてください。");
   if(!string.IsNullOrWhiteSpace(p.Fps))throw new ArgumentException("透過ロゴ消しはFPS指定との併用に未対応です。FPS指定を空欄にしてください。");
   if(AdvancedOptions.Enabled(p))throw new ArgumentException("透過ロゴ消しは詳細パイプライン（混在VFR・10bit・色空間変換など）との併用に未対応です。");
  }
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
  public static string Script(string lsmash,string delogo,string source,string logo,double fps) =>
   "ClearAutoloadDirs()\nLoadPlugin("+Avs(lsmash)+")\nLoadPlugin("+Avs(delogo)+")\n"+
   "v=LWLibavVideoSource("+Avs(source)+",threads=4,repeat=false)\n"+
   "v.DelogoHD(logofile="+Avs(logo)+").AssumeFPS("+((int)Math.Round(fps*1000000)).ToString(CultureInfo.InvariantCulture)+",1000000)\n";
  static async Task EnsureSingleVideo(Job job,Settings settings,CancellationToken token) {
   var probe=await Processes.Run(settings.Ffprobe,new[]{"-v","error","-show_streams","-of","json",job.MediaInput},token,null,60);
   if(probe.ExitCode!=0)throw new InvalidDataException("ロゴ消しの入力ストリームを確認できません。");
   var data=Store.Json.Deserialize<Dictionary<string,object>>(probe.Output);
   int videos=MetadataReader.Items(data,"streams").Select(MetadataReader.Obj).Count(x=>MetadataReader.Text(x,"codec_type")=="video");
   if(videos!=1)throw new InvalidDataException("透過ロゴ消しは映像ストリームが1本のTSだけに対応しています（検出："+videos+"本）。");
  }
  public static async Task<string> Prepare(Job job,Settings settings,string data,CancellationToken token,Action<string> log,Action changed) {
   ValidateCombination(job.Preset);
   string logo=ResolveLogo(data,job,settings);
   string plugin=settings.DelogoPlugin,host=settings.Avs2PipeMod;
   if(!File.Exists(plugin))throw new FileNotFoundException("DelogoHDプラグインがありません。ツール設定で「ロゴ消し · DelogoHD」をダウンロードして適用してください。",plugin);
   if(!File.Exists(host))throw new FileNotFoundException("avs2pipemodがありません。ツール設定で「ロゴ消し · avs2pipemod」をダウンロードして適用してください。",host);
   string bin=string.IsNullOrWhiteSpace(settings.ChapterExe)?"":Path.GetDirectoryName(settings.ChapterExe);
   string lsmash=bin==""?"":Path.Combine(bin,"plugins64","LSMASHSource.dll"),avisynth=bin==""?"":Path.Combine(bin,"AviSynth.dll");
   if(lsmash==""||!File.Exists(lsmash)||!File.Exists(avisynth))throw new FileNotFoundException("CM解析用のAviSynth／L-SMASHがありません。ツール設定でCM解析コンポーネントをダウンロードして適用してください。",lsmash);
   await EnsureSingleVideo(job,settings,token);
   string sourceFingerprint=Store.Fingerprint(job.MediaInput);
   string folder=EncodeCache.WorkFolder(data,settings,job);
   string output=Path.Combine(folder,"delogo-video.mkv"),receiptFile=output+".json";
   string key=sourceFingerprint+"|delogo-v1|"+Store.Fingerprint(logo)+"|"+Store.Fingerprint(plugin)+"|"+Store.Fingerprint(host)+"|"+Store.Fingerprint(lsmash);
   var receipt=Store.Load(receiptFile,()=>new DelogoReceipt());
   if(File.Exists(output)&&receipt.Source==key&&receipt.Output==Store.Fingerprint(output)) {
    try{await ValidateIntermediate(settings,output,job.Metadata,token);log("ロゴ消し済み映像をキャッシュから再利用します。");return output;}catch(InvalidDataException){log("ロゴ消し済み映像のキャッシュが壊れているため作り直します。");}
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
   if(!File.Exists(privateIndex)||Store.Load(receiptFile,()=>new DelogoReceipt()).Source!=key) {
    string probeScript=Path.Combine(folder,"delogo-probe.avs");
    File.WriteAllText(probeScript,"ClearAutoloadDirs()\nLoadPlugin("+Avs(runtimeLsmash)+")\nLWLibavVideoSource("+Avs(job.MediaInput)+",threads=4,repeat=false)\n",encoding);
    string sharedIndex=Path.GetFullPath(job.MediaInput)+".lwi",backup=Path.Combine(folder,"previous-delogo-index.lwi");
    bool hadIndex=File.Exists(sharedIndex);if(hadIndex)File.Move(sharedIndex,backup);
    try {
     var info=await Processes.Run(hostExe,new[]{"-info",probeScript},token,log);
     if(info.ExitCode!=0)throw new InvalidDataException("ロゴ消し用インデックスを作成できません："+Converter.Tail(info.Error));
     if(!File.Exists(sharedIndex))throw new InvalidDataException("ロゴ消し用インデックスを作成できません。入力フォルダーの書き込み権限を確認してください。");
     File.Copy(sharedIndex,privateIndex,true);
    }finally {
     if(File.Exists(sharedIndex))File.Delete(sharedIndex);
     if(hadIndex&&File.Exists(backup))File.Move(backup,sharedIndex);
    }
   }
   string script=Path.Combine(folder,"delogo.avs");
   File.WriteAllText(script,Script(runtimeLsmash,runtimePlugin,privateIndex,logo,job.Metadata.Fps),encoding);
   string pending=output+".partial.mkv";
   try {
    var producer=new Command {Exe=hostExe,Args=new List<string>{PipeMode(job.Metadata),"-dll="+Path.Combine(runtime,"AviSynth.dll"),script}};
    var consumer=new Command {Exe=settings.Ffmpeg,Args=new List<string>{"-hide_banner","-nostdin","-y","-f","yuv4mpegpipe","-i","-","-an","-c:v","ffv1","-level","3",pending}};
    log(producer+" | "+consumer);
    await PipeEncoding.Run(producer,consumer,token,log,"avs2pipemod","FFmpeg");
    token.ThrowIfCancellationRequested();
    if(Store.Fingerprint(job.MediaInput)!=sourceFingerprint)throw new IOException("ロゴ消し中に入力ファイルが更新されました。");
    await ValidateIntermediate(settings,pending,job.Metadata,token);
    File.Move(pending,output,true);
   }finally {if(File.Exists(pending))File.Delete(pending);}
   Store.Save(receiptFile,new DelogoReceipt {Source=key,Output=Store.Fingerprint(output)});
   log("ロゴ消し済み映像を作成しました："+output);return output;
  }
  static async Task ValidateIntermediate(Settings settings,string file,Metadata m,CancellationToken token) {
   await AdvancedEncoder.ValidateVideo(settings,file,m.Duration,token);
   var probe=await Processes.Run(settings.Ffprobe,new[]{"-v","error","-show_streams","-of","json",file},token,null,60);
   if(probe.ExitCode!=0)throw new InvalidDataException("ロゴ消し済み映像を読み取れません。");
   var data=Store.Json.Deserialize<Dictionary<string,object>>(probe.Output);
   var video=MetadataReader.Items(data,"streams").Select(MetadataReader.Obj).FirstOrDefault(x=>MetadataReader.Text(x,"codec_type")=="video");
   if(video==null||MetadataReader.Int(video,"width")!=m.Width||MetadataReader.Int(video,"height")!=m.Height)throw new InvalidDataException("ロゴ消し済み映像の解像度が元TSと一致しません。");
  }
 }
}
