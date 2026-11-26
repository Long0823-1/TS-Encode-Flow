using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TsEncodeFlow;

namespace EncodeFlow.App.Linux;

// 自動化画面の入力値と保存処理（Windows版 AutomationView の保存経路に対応）。
// 画面と検証用スモークの両方が ApplyAutomation を通る。
sealed class AutomationValues {
 public string WatchFolder="",QuietSecondsText="60";
 public bool WatchEnabled,Recursive;
 public int Parallel=1;
 public static AutomationValues Capture(Settings s)=>new() {
  WatchFolder=s.WatchFolder,QuietSecondsText=s.QuietSeconds.ToString(),
  WatchEnabled=s.WatchEnabled,Recursive=s.Recursive,Parallel=s.Parallel};
 public void ApplyTo(Settings s) {
  if(!int.TryParse(QuietSecondsText,out int seconds))throw new ArgumentException("待機秒数は整数で入力してください。");
  if(WatchEnabled&&!Directory.Exists(WatchFolder))throw new ArgumentException("録画フォルダーを選択してください。");
  s.WatchFolder=WatchFolder??"";s.WatchEnabled=WatchEnabled;s.Recursive=Recursive;s.QuietSeconds=seconds;s.Parallel=Parallel;
 }
}
static class AutomationScreenLogic {
 public static void ApplyAutomation(EngineClientLinux client,AutomationValues values) {
  values.ApplyTo(client.Settings);client.Save();client.Wake();
 }
}

// 自動化画面（Windows版 AutomationView の対応物）。
// 録画後BAT／EPGTimer連携は Windows 専用のため、Linux では無効化して注記する（設定値そのものは共有される）。
sealed class AutomationScreen:UserControl {
 readonly EngineClientLinux client;readonly Action<string> notice;
 readonly TextBox folder,quiet;readonly ToggleSwitch watcher,recurse;readonly NumericUpDown parallel;readonly TextBlock parallelLabel;

 public AutomationScreen(EngineClientLinux client,Action<string> notice) {
  this.client=client;this.notice=notice;var s=client.Settings;
  Preset batPreset=s.BatPresetIndex>=0&&s.BatPresetIndex<s.Presets.Count?s.Presets[s.BatPresetIndex]:client.Selected;
  var batEngine=Ui.Picker(Enum.GetValues<Engine>().Select(PresetCatalog.EngineName).ToList(),(int)batPreset.Engine);
  var batPresetPicker=Ui.Picker(s.Presets.Where(p=>p.Engine==batPreset.Engine).Select(p=>p.Name+(p.Category=="カスタム"?"（カスタム）":"")).ToList(),
   Math.Max(0,s.Presets.Where(p=>p.Engine==batPreset.Engine).ToList().IndexOf(batPreset)));
  var followLatest=new ToggleSwitch {IsChecked=true};
  var batCard=Ui.Card(Ui.Stack(Ui.Pill("録画後の自動変換"),Ui.Text("録画が終わったら、自動で変換",24,bold:true),
   Ui.Note("EPGTimerの録画後実行に登録するBATを作成します。\n画面を閉じていても、ワーカーがキューを処理します。"),
   Ui.Field("変換方式",batEngine),Ui.Field("録画後BATに使うプリセット",batPresetPicker,"手動変換用とは別に選べます。自分で保存したカスタムプリセットも利用できます。"),
   Ui.Text(batPreset.Name+" · "+PresetCatalog.EngineName(batPreset.Engine)+"\n"+batPreset.Codec+" · "+batPreset.Container+" · "+PresetCatalog.ResolutionName(batPreset),16,bold:true),
   Ui.Row(Ui.Text("プリセットの変更に自動追従"),followLatest),
   Ui.Note("ON：実行時の最新プリセット・マクロ・動画保存先を使用。OFF：作成時の設定を固定。登録後のキューは変更しません。"),
   Ui.Button("このプリセットで録画後BATを作成",()=>{},bold:true),
   Ui.Note("※ EPGTimer／録画後BATは Windows 専用の仕組みのため、Linux 版では利用できません（この画面では無効化しています）。プリセットの指定（BatPresetIndex）などの設定値自体は Windows 版と共有されます。Linux での自動変換には下のフォルダー監視を使ってください。")),selected:true);
  batCard.IsEnabled=false;
  folder=Ui.Entry(s.WatchFolder,"録画TSが保存されるフォルダー");
  watcher=new ToggleSwitch {IsChecked=s.WatchEnabled};
  quiet=Ui.Entry(s.QuietSeconds.ToString());
  recurse=new ToggleSwitch {IsChecked=s.Recursive};
  var browse=Ui.Button("フォルダーを選択",async()=>{string? path=await PickFolder();if(path!=null)folder.Text=path;});
  var watch=Ui.Card(Ui.Stack(Ui.Text("フォルダー監視でも自動化",21,bold:true),
   Ui.Note("録画中のTSは、更新が止まり書き込みが終わるまで待ちます。\n確実な録画終了通知には、Windows版のBAT連携をおすすめします。"),
   Ui.Field("録画フォルダー",folder),browse,
   Ui.Row(Ui.Text("サブフォルダーも監視",13),recurse),
   Ui.Field("更新が止まってから待つ秒数",quiet),
   Ui.Row(Ui.Text("監視を有効にする",15,bold:true),watcher)));
  parallel=new NumericUpDown {Minimum=1,Maximum=8,Increment=1,Value=s.Parallel,MinWidth=140};
  parallelLabel=Ui.Text(s.Parallel+"本",21,bold:true);
  parallel.ValueChanged+=(_,_)=>parallelLabel.Text=(int)(parallel.Value??1)+"本";
  var parallelCard=Ui.Card(Ui.Stack(Ui.Text("一度に変換する本数",21,bold:true),
   Ui.Row(parallelLabel,parallel),
   Ui.Note("GPU・CPUの負荷やストレージ速度に合わせて調整してください。")));
  var save=Ui.Button("自動処理の設定を保存",Save,bold:true);
  Content=Ui.Scroll(Ui.Stack(batCard,watch,parallelCard,save));
 }
 public void Reload() {
  var v=AutomationValues.Capture(client.Settings);
  folder.Text=v.WatchFolder;quiet.Text=v.QuietSecondsText;watcher.IsChecked=v.WatchEnabled;recurse.IsChecked=v.Recursive;
  parallel.Value=v.Parallel;parallelLabel.Text=v.Parallel+"本";
 }
 void Save() {
  try {
   var v=new AutomationValues {
    WatchFolder=folder.Text??"",QuietSecondsText=quiet.Text??"",
    WatchEnabled=watcher.IsChecked==true,Recursive=recurse.IsChecked==true,Parallel=(int)(parallel.Value??1)};
   AutomationScreenLogic.ApplyAutomation(client,v);
   notice("自動処理の設定を保存しました。");
  }catch(Exception e){notice(e.Message);}
 }
 async Task<string?> PickFolder() {
  var top=TopLevel.GetTopLevel(this);if(top==null)return null;
  var folders=await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {Title="録画フォルダーを選択",AllowMultiple=false});
  return folders.Count>0?folders[0].TryGetLocalPath():null;
 }
}
