using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TsEncodeFlow {
 public enum Engine { CPU, NVEncC, VCEEncC, QSVEncC }
 public enum Codec { H264, HEVC, AV1 }
 public enum Container { mp4, mkv }
 public enum RateMode { Quality, VBR, CBR }
 public enum Deinterlace { None, Yadif, YadifBob, Bwdif, BwdifBob }
 public enum State { Waiting, Running, Completed, Failed, Cancelled, Interrupted, NeedsInput }
 [TypeConverter(typeof(ExpandableObjectConverter))]
 public sealed class Preset {
  [Browsable(false)] public string FactoryId {get;set;}="";
  [Browsable(false)] public string Category {get;set;}="カスタム";
  [Browsable(false)] public string Summary {get;set;}="自分好みの変換設定";
  [Browsable(false)] public string Notes {get;set;}="";
  [Category("1. 変換"),DisplayName("名前")] public string Name {get;set;}="CPU H.264";
  [Category("1. 変換"),DisplayName("エンコーダー")] public Engine Engine {get;set;}=Engine.CPU;
  [Category("1. 変換"),DisplayName("コーデック")] public Codec Codec {get;set;}=Codec.H264;
  [Category("1. 変換"),DisplayName("コンテナー")] public Container Container {get;set;}=Container.mp4;
  [Category("1. 変換"),DisplayName("レート制御")] public RateMode RateMode {get;set;}=RateMode.Quality;
  [Category("1. 変換"),DisplayName("品質値"),Description("CPU: CRF、NVEncC/VCEEncC: CQP、QSVEncC: ICQ。小さいほど高画質。")]
  public int Quality {get;set;}=23;
  [Category("1. 変換"),DisplayName("映像kbps")] public int Bitrate {get;set;}=5000;
  [Category("1. 変換"),DisplayName("速度プリセット"),Description("CPU: fast/medium/slow、NVEncC: P1～P7、VCEEncC: balanced/speed/quality、QSVEncC: balanced/best/faster等。空欄なら各エンコーダーの標準。")]
  public string Speed {get;set;}="";
  [Category("1. 変換"),DisplayName("CPUスレッド数"),Description("0=自動。並列処理時は少なめに設定。")]
  public int Threads {get;set;}=0;
  [Category("2. 解像度"),DisplayName("幅"),Description("0=自動。幅・高さが両方0なら維持。")]
  public int Width {get;set;}=0;
  [Category("2. 解像度"),DisplayName("高さ")] public int Height {get;set;}=0;
  [Category("2. 解像度"),DisplayName("縦横比を保持"),Description("指定枠に収めます。黒帯は追加しません。放送のピクセル縦横比も考慮します。")]
  public bool KeepAspect {get;set;}=true;
  [Category("2. 解像度"),DisplayName("拡大を許可")] public bool AllowUpscale {get;set;}=false;
  [Category("3. フィルタ"),DisplayName("インターレース解除")] public Deinterlace Deinterlace {get;set;}=Deinterlace.Yadif;
  [Category("3. フィルタ"),DisplayName("フレームレート"),Description("空欄=維持。例: 30000/1001、60000/1001、24。GPU変換では指定時にFFmpegの可逆一時ファイルで前処理します（空き容量が必要）。")]
  public string Fps {get;set;}="";
  [Category("3. フィルタ"),DisplayName("クロップ左")] public int CropLeft {get;set;}=0;
  [Category("3. フィルタ"),DisplayName("クロップ上")] public int CropTop {get;set;}=0;
  [Category("3. フィルタ"),DisplayName("クロップ右")] public int CropRight {get;set;}=0;
  [Category("3. フィルタ"),DisplayName("クロップ下")] public int CropBottom {get;set;}=0;
  [Category("3. フィルタ"),DisplayName("ノイズ除去"),Description("CPU: hqdn3d、GPU: VPP KNN。")]
  public bool Denoise {get;set;}=false;
  [Category("3. フィルタ"),DisplayName("シャープ化")] public bool Sharpen {get;set;}=false;
  public bool KeepSubtitles {get;set;}=true;
  public bool IgnoreSubtitleErrors {get;set;}=false;
  public bool EmbedMetadata {get;set;}=true;
  [Category("4. 音声"),DisplayName("音声をコピー"),Description("false=AACに変換。最初の選択サービス内の音声1本を対象にします。")]
  public bool CopyAudio {get;set;}=false;
  [Category("4. 音声"),DisplayName("AAC kbps")] public int AudioBitrate {get;set;}=192;
  [Category("5. 詳細"),DisplayName("サービスID"),Description("0=最高解像度の映像があるサービスを選択。複数番組TSでは対象SIDを明示できます。")]
  public int ServiceId {get;set;}=0;
  [Category("5. 詳細"),DisplayName("追加引数"),Description("選択エンコーダーに渡す追加引数。シェルは使いません。入出力ファイルや上書き指定の変更は不可。")]
  public string ExtraArgs {get;set;}="";
  public override string ToString()=>Name;
 }
 public sealed class Settings {
  [Category("1. 自動処理"),DisplayName("監視を有効にする")] public bool WatchEnabled {get;set;}=false;
  [Category("1. 自動処理"),DisplayName("録画フォルダー")] public string WatchFolder {get;set;}="";
  [Category("1. 自動処理"),DisplayName("サブフォルダーも監視")] public bool Recursive {get;set;}=false;
  [Category("1. 自動処理"),DisplayName("更新が止まってから待つ秒数"),Description("録画中ファイルを避けるため、サイズと更新時刻の安定・書き込みロックを確認します。録画後バッチ連携を推奨。")]
  public int QuietSeconds {get;set;}=60;
  [Category("1. 自動処理"),DisplayName("同時変換数")] public int Parallel {get;set;}=1;
  [Category("2. 出力"),DisplayName("保存先")] public string OutputFolder {get;set;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"TS-Encode-Flow");
  [Category("2. 出力"),DisplayName("ファイル名マクロ"),Description("例: {Start:yyyyMMdd_HHmm}_{Channel}_{Title}。相対サブフォルダーも可。拡張子は自動。")]
  public string FileName {get;set;}="{Start:yyyyMMdd_HHmm}_{Channel}_{Title}";
  [Category("3. ツール"),DisplayName("ffmpeg.exe")] public string Ffmpeg {get;set;}="";
  [Category("3. ツール"),DisplayName("ffprobe.exe")] public string Ffprobe {get;set;}="";
  [Category("3. ツール"),DisplayName("NVEncC64.exe")] public string Nvenc {get;set;}="";
  [Category("3. ツール"),DisplayName("VCEEncC64.exe")] public string Vceenc {get;set;}="";
  [Category("3. ツール"),DisplayName("QSVEncC64.exe")] public string Qsvenc {get;set;}="";
  [Category("3. ツール"),DisplayName("EDCB EpgDataCap3.dll"),Description("任意。TSのEIT/SDTを日本語にデコードするEDCB x64のDLL。未指定でも.program.txtを利用できます。")]
  public string EpgDll {get;set;}="";
  [Browsable(false)] public string CaptionTool {get;set;}="";
  [Browsable(false)] public List<Preset> Presets {get;set;}=PresetCatalog.Create();
  [Browsable(false)] public int SelectedPreset {get;set;}=0;
  [Browsable(false)] public int BatPresetIndex {get;set;}=-1;
  public void Validate() {
   if(Parallel<1||Parallel>8)throw new ArgumentException("同時変換数は1～8です。");
   if(QuietSeconds<3||QuietSeconds>86400)throw new ArgumentException("安定待ちは3～86400秒です。");
   if(string.IsNullOrWhiteSpace(OutputFolder)||string.IsNullOrWhiteSpace(FileName))throw new ArgumentException("保存先とファイル名マクロを指定してください。");
   if(WatchEnabled&&string.Equals(Path.GetFullPath(WatchFolder).TrimEnd('\\'),Path.GetFullPath(OutputFolder).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("録画フォルダーと保存先は分けてください。");
   if(Presets==null||Presets.Count==0)throw new ArgumentException("プリセットが必要です。");
  }
 }
 public sealed class Metadata {
  public string Title="",Channel="",Description="",Episode="",Source="",OriginalName="";
  public DateTime Start;
  public int ServiceId,EventId,NetworkId,TransportId,Width,Height,VideoIndex,AudioIndex=-1,SubtitleIndex=-1,SubtitlePid=-1;
  public string SubtitleCodec="";
  public string SubtitleStatus="";
  public string SubtitleWarning="";
  public long SubtitlePackets;
  public double Sar=1,Duration,Fps,VideoStart;
 }
 public sealed class Job {
  public string Id=Guid.NewGuid().ToString("N"),Input,Output="",Fingerprint,Message="",LogFile="",Command="";
  public State State=State.Waiting;
  public double Progress;
  public DateTime Created=DateTime.Now;
  public Preset Preset;
  public string OutputFolder,FileName;
  public Metadata Metadata;
  public List<DrcsGlyph> Glyphs=new List<DrcsGlyph>();
 }
 public static class Store {
  static Store(){Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);}
  public static JsonCodec Json=>new JsonCodec();
  public static T Clone<T>(T value)=>Json.Deserialize<T>(Json.Serialize(value));
  public static void Save(string file,object value) {
   Directory.CreateDirectory(Path.GetDirectoryName(file));string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
   File.WriteAllText(temp,Json.Serialize(value),new UTF8Encoding(false));
   if(File.Exists(file))File.Replace(temp,file,file+".bak");else File.Move(temp,file);
  }
  public static T Load<T>(string file,Func<T> factory) {
   if(!File.Exists(file))return factory();
   try {
    for(int attempt=0;;attempt++)try{using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream))return Json.Deserialize<T>(reader.ReadToEnd());}catch(IOException)when(attempt<4){System.Threading.Thread.Sleep(20);}
   }catch(Exception e){throw new InvalidDataException("設定の読み込みに失敗しました。ファイルを保持しています："+file,e);}
  }
  public static string FindTool(params string[] names) {
   foreach(string dir in new[]{Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Tools"),AppDomain.CurrentDomain.BaseDirectory}.Concat((Environment.GetEnvironmentVariable("PATH")??"").Split(';')))
    foreach(string name in names)try{string path=Path.Combine(dir.Trim('"'),name);if(File.Exists(path))return Path.GetFullPath(path);}catch(ArgumentException){}
   return "";
  }
  public static void Detect(Settings s) {
   if(!File.Exists(s.Ffmpeg))s.Ffmpeg=FindTool("ffmpeg.exe");if(!File.Exists(s.Ffprobe))s.Ffprobe=FindTool("ffprobe.exe");
   if(!File.Exists(s.Nvenc))s.Nvenc=FindTool("NVEncC64.exe","NVEncC.exe");if(!File.Exists(s.Vceenc))s.Vceenc=FindTool("VCEEncC64.exe","VCEEncC.exe");if(!File.Exists(s.Qsvenc))s.Qsvenc=FindTool("QSVEncC64.exe","QSVEncC.exe");
   if(!File.Exists(s.EpgDll))s.EpgDll=FindTool("EpgDataCap3.dll");
   if(!File.Exists(s.CaptionTool))s.CaptionTool=FindTool("CaptionProbe.exe");
  }
  public static string Fingerprint(string file) {var f=new FileInfo(file);return f.FullName.ToUpperInvariant()+"|"+f.Length+"|"+f.LastWriteTimeUtc.Ticks;}
 }
 public static class Names {
  public static string Safe(string value) {
   var invalid=Path.GetInvalidFileNameChars();string text=new string((value??"").Select(c=>invalid.Contains(c)||char.IsControl(c)?'_':c).ToArray()).Trim().TrimEnd('.');
   if(text.Length>120)text=text.Substring(0,120).TrimEnd('.',' ');
   if(Regex.IsMatch(text,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase))text="_"+text;
   return text;
  }
  public static string Render(string template,Metadata m,Preset p) {
   var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"Title",string.IsNullOrWhiteSpace(m.Title)?m.OriginalName:m.Title},{"Channel",string.IsNullOrWhiteSpace(m.Channel)?"不明局":m.Channel},{"OriginalName",m.OriginalName},{"ServiceId",m.ServiceId.ToString()},{"EventId",m.EventId.ToString()},{"NetworkId",m.NetworkId.ToString()},{"TransportId",m.TransportId.ToString()},{"Episode",m.Episode},{"Width",m.Width.ToString()},{"Height",m.Height.ToString()},{"Codec",p.Codec.ToString()},{"Engine",p.Engine.ToString()}};
   string result=Regex.Replace(template,@"\{([^{}:]+)(?::([^{}]+))?\}",match=> {
    if(match.Groups[1].Value.Equals("Start",StringComparison.OrdinalIgnoreCase))return Safe(m.Start.ToString(match.Groups[2].Success?match.Groups[2].Value:"yyyyMMdd_HHmm",CultureInfo.InvariantCulture));
    string value;if(!values.TryGetValue(match.Groups[1].Value,out value))throw new ArgumentException("未知のマクロ："+match.Value);return Safe(value);
   });
   if(result.Contains("{")||result.Contains("}")||Path.IsPathRooted(result))throw new ArgumentException("ファイル名マクロが不正です。");
   var parts=result.Replace('\\','/').Split('/');if(parts.Any(x=>x==".."||x=="."||string.IsNullOrWhiteSpace(x)))throw new ArgumentException("出力先の相対パスが不正です。");
   return Path.Combine(parts.Select(Safe).ToArray())+"."+p.Container;
  }
 }
}
