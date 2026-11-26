using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TsEncodeFlow;

namespace EncodeFlow.App.Linux;

// 設定画面の入力値と保存処理（Windows版 SettingsView の保存経路に対応）。
// 画面と検証用スモークの両方が ApplySettings を通る。
sealed class SettingsValues {
 public string OutputFolder="",FileName="",EncodeCacheFolder="",VaapiDevice="";
 public int Parallel=1;
 public Dictionary<string,string> Tools=new();
 public static readonly (string Key,string Label,string Filter)[] ToolDefs=[
  ("Ffmpeg","FFmpeg · 必須",""),("Ffprobe","ffprobe · 必須",""),("Nvenc","NVEncC · NVIDIA",""),("Vceenc","VCEEncC · AMD",""),
  ("Qsvenc","QSVEncC · Intel",""),("EpgDll","EpgDataCap3.dll · 番組情報","*.dll"),("CaptionTool","CaptionProbe · 字幕解析",""),
  ("ChapterExe","chapter_exe · CM解析の構成ツール",""),("LogoFrame","logoframe · ロゴ検出",""),("DelogoPlugin","DelogoHD.dll · ロゴ消しプラグイン","*.dll"),
  ("CmLogo","対象放送局のロゴLGD · 空欄＝自動生成","*.lgd"),("JoinLogoScript","join_logo_scpスクリプト · 空欄＝JL_標準.txt","*.txt")];
 public static SettingsValues Capture(Settings s) {
  var v=new SettingsValues {OutputFolder=s.OutputFolder,FileName=s.FileName,EncodeCacheFolder=s.EncodeCacheFolder,VaapiDevice=s.VaapiDevice,Parallel=s.Parallel};
  foreach(var (key,_,_) in ToolDefs)v.Tools[key]=(string)typeof(Settings).GetProperty(key)!.GetValue(s)!;
  return v;
 }
 public void ApplyTo(Settings s) {
  foreach(var pair in Tools)typeof(Settings).GetProperty(pair.Key)!.SetValue(s,pair.Value);
  s.VaapiDevice=VaapiDevice;s.OutputFolder=OutputFolder;s.FileName=FileName;s.EncodeCacheFolder=EncodeCacheFolder;s.Parallel=Parallel;
 }
}
static class SettingsScreenLogic {
 public static void ApplySettings(EngineClientLinux client,SettingsValues values) {values.ApplyTo(client.Settings);client.Save();}
}

// 設定画面（Windows版 SettingsView の対応物）。
sealed class SettingsScreen:UserControl {
 readonly EngineClientLinux client;readonly Action<string> notice;
 readonly TextBox output,macro,cache,vaapiDevice;readonly TextBlock preview,cachePath;
 readonly Dictionary<string,TextBox> toolInputs=new();readonly Dictionary<string,TextBlock> toolStatus=new();
 readonly NumericUpDown parallel;bool sampleEpisode=true;

 public SettingsScreen(EngineClientLinux client,Action<string> notice) {
  this.client=client;this.notice=notice;var s=client.Settings;
  output=Ui.Entry(s.OutputFolder);macro=Ui.Entry(s.FileName);cache=Ui.Entry(s.EncodeCacheFolder);vaapiDevice=Ui.Entry(s.VaapiDevice,"空欄なら /dev/dri/renderD128");
  preview=Ui.Text("",13);cachePath=Ui.Text("",12,muted:true);
  macro.TextChanged+=(_,_)=>Preview();cache.TextChanged+=(_,_)=>CachePreview();
  var outputRow=Ui.Row(output,Ui.Button("保存先を選択",async()=>{string? path=await PickFolder();if(path!=null)output.Text=path;}));
  var basic=Ui.Card(Ui.Stack(Ui.Text("保存先とファイル名",21,bold:true),
   Ui.Field("動画の保存先",outputRow),
   Ui.Field("ファイル名マクロ",macro,"{Title} {TitleWithoutEpisode} {Channel} {Start:yyyyMMdd_HHmm} {Episode} {OriginalName} {ServiceId} {EventId} {Codec} {Engine}\n条件分岐：{if:Episode}{Title}_第{Episode}話{else}{Title}{endif}\n比較：{if:Width>=1920}HD{else}SD{endif} / {if:Channel==局名}…{endif}"),
   preview,Ui.Button("話数あり／なしのプレビューを切り替え",()=>{sampleEpisode=!sampleEpisode;Preview();})));
  var cacheCard=Ui.Card(Ui.Stack(Ui.Text("エンコード用キャッシュ",21,bold:true),
   Ui.Field("キャッシュの保存先（空欄＝標準）",cache),
   Ui.Row(Ui.Button("キャッシュ先を選択",async()=>{string? path=await PickFolder();if(path!=null){cache.Text=path;SaveCache();}}),
          Ui.Button("標準に戻す",()=>{cache.Text="";SaveCache();}),
          Ui.Button("キャッシュ設定を保存",SaveCache,bold:true)),
   cachePath,
   Ui.Note("フォルダー選択・標準に戻す操作はその場で保存します。手入力した場合は「キャッシュ設定を保存」を押してください。\n再開用の映像区間・GPU変換の一時ファイル・CM解析用の一時PCM音声を保存します。指定先には専用のTS-Encode-Flow-Cacheフォルダーを作成します。\n動画の保存・検証が成功したら、このジョブのキャッシュを自動削除します。失敗・中止時は再試行用に保持します。\n保存後に開始する処理から適用します。実行中の処理や既存ファイルを移動しません。CM解析結果・インデックス・字幕・比較画像・ツール・設定は従来の場所です。")));
  var toolsStack=Ui.Stack(Ui.Text("変換ツール",21,bold:true),
   Ui.Note("FFmpegとffprobeはすべての方式で必要です。\n日本語番組名のTS解析には、EDCB x64のEpgDataCap3.dllを指定できます。CMツール（chapter_exe / logoframe / join_logo_scp）は scripts/build-cm-tools.sh で用意したものを指定してください。"),
   Ui.Button("ツールを自動検出",Detect,bold:true));
  foreach(var (key,label,filter) in SettingsValues.ToolDefs) {
   string k=key;var entry=Ui.Entry((string)typeof(Settings).GetProperty(k)!.GetValue(s)!);
   var status=Ui.Text("",11,muted:true);
   toolInputs[k]=entry;toolStatus[k]=status;
   var row=Ui.Row(entry,Ui.Button("選択",async()=>{string? path=await PickFile(filter);if(path!=null){entry.Text=path;Status(k);}}));
   toolsStack.Children.Add(Ui.Field(label,row));
   toolsStack.Children.Add(status);
  }
  toolsStack.Children.Add(Ui.Field("VA-APIデバイス（Linux）",vaapiDevice,"VA-APIエンコードで使うDRMデバイス。空欄なら /dev/dri/renderD128。"));
  var toolsCard=Ui.Card(toolsStack);
  parallel=new NumericUpDown {Minimum=1,Maximum=8,Increment=1,Value=s.Parallel,MinWidth=140};
  var processCard=Ui.Card(Ui.Stack(Ui.Text("処理",21,bold:true),
   Ui.Field("同時変換数（1～8）",parallel,"「自動化」画面の同時変換数と同じ設定です。GPU・CPUの負荷やストレージ速度に合わせて調整してください。")));
  var setupNote=Ui.Card(Ui.Stack(Ui.Text("ツールの準備について",21,bold:true),
   Ui.Note("Windows版にある「ツールをまとめてダウンロード」は Windows x64 バイナリ専用のため、Linux 版にはありません。Linux では OS のパッケージや配布バイナリを用意し、上の「ツールを自動検出」か手動指定で割り当ててください。CaptionProbe は scripts/build-native.sh、CMツール一式は scripts/build-cm-tools.sh でビルドできます。")));
  var save=Ui.Button("設定を保存",Save,bold:true);
  var folderCard=Ui.Card(Ui.Stack(Ui.Text("設定・ログ",20,bold:true),Ui.Text(client.Data,11,muted:true),
   Ui.Button("設定フォルダーを開く",()=>{try{Process.Start(new ProcessStartInfo("xdg-open",client.Data){UseShellExecute=false});}catch(Exception e){notice(e.Message);}})));
  Content=Ui.Scroll(Ui.Stack(basic,cacheCard,toolsCard,processCard,setupNote,save,folderCard));
  Preview();CachePreview();foreach(var key in toolInputs.Keys)Status(key);
 }
 public void Reload() {
  var v=SettingsValues.Capture(client.Settings);
  output.Text=v.OutputFolder;macro.Text=v.FileName;cache.Text=v.EncodeCacheFolder;vaapiDevice.Text=v.VaapiDevice;parallel.Value=v.Parallel;
  foreach(var pair in toolInputs){pair.Value.Text=v.Tools[pair.Key];Status(pair.Key);}
  Preview();CachePreview();
 }
 void Status(string key) {
  string path=toolInputs[key].Text??"";
  toolStatus[key].Text=path.Length==0?"未指定":File.Exists(path)?"検出済み："+path:"未検出（ファイルがありません）";
 }
 void Detect() {
  Store.Detect(client.Settings);
  foreach(var pair in toolInputs){pair.Value.Text=(string)typeof(Settings).GetProperty(pair.Key)!.GetValue(client.Settings)!;Status(pair.Key);}
  notice("ツールを自動検出しました。「設定を保存」で確定します。");
 }
 void Preview() {
  try{preview.Text="出力例（話数"+(sampleEpisode?"あり":"なし")+"）："+Names.Render(macro.Text??"",new Metadata {Title="お気に入りの番組 第12話",Channel="テスト放送",OriginalName="録画",Start=DateTime.Today.AddHours(21),Episode=sampleEpisode?"12":"",Width=1920,Height=1080},client.Selected);}
  catch(Exception e){preview.Text=e.Message;}
 }
 void CachePreview() {
  try{bool saved=string.Equals(cache.Text?.Trim()??"",client.Settings.EncodeCacheFolder??"",StringComparison.OrdinalIgnoreCase);
   cachePath.Text=(saved?"保存済みの保存先：":"未保存の保存先：")+EncodeCache.Root(client.Data,cache.Text??"");}
  catch(Exception e){cachePath.Text=e.Message;}
 }
 void SaveCache() {
  try{client.SaveCacheFolder(cache.Text);cache.Text=client.Settings.EncodeCacheFolder;CachePreview();notice("キャッシュ先を保存しました。次に開始する処理から適用します。");}
  catch(Exception e){notice(e.Message);}
 }
 void Save() {
  try {
   var v=SettingsValues.Capture(client.Settings);
   v.OutputFolder=output.Text??"";v.FileName=macro.Text??"";v.EncodeCacheFolder=cache.Text?.Trim()??"";v.VaapiDevice=vaapiDevice.Text?.Trim()??"";
   v.Parallel=(int)(parallel.Value??1);
   foreach(var pair in toolInputs)v.Tools[pair.Key]=pair.Value.Text??"";
   SettingsScreenLogic.ApplySettings(client,v);
   Reload();
   notice("保存先・キャッシュ・ファイル名・ツールの設定を保存しました。");
  }catch(Exception e){notice(e.Message);}
 }
 async Task<string?> PickFolder() {
  var top=TopLevel.GetTopLevel(this);if(top==null)return null;
  var folders=await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {Title="フォルダーを選択",AllowMultiple=false});
  return folders.Count>0?folders[0].TryGetLocalPath():null;
 }
 async Task<string?> PickFile(string filter) {
  var top=TopLevel.GetTopLevel(this);if(top==null)return null;
  var type=filter.Length==0?new FilePickerFileType("すべてのファイル"){Patterns=["*"]}:new FilePickerFileType(filter){Patterns=[filter]};
  var files=await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {Title="ファイルを選択",AllowMultiple=false,FileTypeFilter=[type]});
  return files.Count>0?files[0].TryGetLocalPath():null;
 }
}
