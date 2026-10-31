using TsEncodeFlow;

namespace EncodeFlow.App;
sealed class AutomationView:ContentView {
 public AutomationView(EngineClient client,Action<string> notice) {
  var s=client.Settings;
  Preset batPreset=s.BatPresetIndex>=0&&s.BatPresetIndex<s.Presets.Count?s.Presets[s.BatPresetIndex]:client.Selected;
  var profile=Pixel.Text("",17,null,true);
  var engine=new Picker();engine.ItemsSource=Enum.GetValues<Engine>().Select(PresetCatalog.EngineName).ToList();engine.SelectedIndex=(int)batPreset.Engine;
  var preset=new Picker();List<Preset> available=[];bool loading=false;
  void UpdateSummary(){profile.Text=batPreset.Name+" · "+PresetCatalog.EngineName(batPreset.Engine)+"\n"+batPreset.Codec+" · "+batPreset.Container+" · "+PresetCatalog.ResolutionName(batPreset);}
  void RefreshPresets(){loading=true;available=s.Presets.Where(p=>(int)p.Engine==engine.SelectedIndex).ToList();preset.ItemsSource=available.Select(p=>p.Name+(p.Category=="カスタム"?"（カスタム）":"")).ToList();int index=available.IndexOf(batPreset);preset.SelectedIndex=index>=0?index:0;if(index<0&&available.Count>0)batPreset=available[0];loading=false;UpdateSummary();}
  engine.SelectedIndexChanged+=(sender,e)=>{RefreshPresets();s.BatPresetIndex=s.Presets.IndexOf(batPreset);client.Save();};
  preset.SelectedIndexChanged+=(sender,e)=>{if(loading||preset.SelectedIndex<0)return;batPreset=available[preset.SelectedIndex];s.BatPresetIndex=s.Presets.IndexOf(batPreset);client.Save();UpdateSummary();notice("録画後BAT用のプリセットを変更しました。新しく作成するBATに反映されます。");};RefreshPresets();
  var bat=Pixel.Card(Pixel.Stack(Pixel.Pill("録画後の自動変換"),Pixel.Text("録画が終わったら、自動で変換",25,null,true),Pixel.Text("EPGTimerの録画後実行に登録するBATを作成します。\n画面を閉じていても、ワーカーがキューを処理します。",14,Pixel.Muted),Pixel.Field("変換方式",engine),Pixel.Field("録画後BATに使うプリセット",preset,"手動変換用とは別に選べます。自分で保存したカスタムプリセットも利用できます。"),profile,Pixel.Button("このプリセットで録画後BATを作成",async()=> {
   client.Save();string? path=await Pickers.Save("TS-Encode-Flow-録画後",".bat");if(path==null)return;
   Integration.SaveBat(path,client.Worker,client.Data,s,batPreset);
   notice("BATとプリセット設定を保存しました。EPGTimerの録画後実行に登録してください。");
   await Application.Current!.Windows[0].Page!.DisplayAlert("BATを作成しました","EPGTimerの録画設定 → 録画後実行に、このBATを指定してください。\n\n作成時のプリセット・保存先・ファイル名マクロを固定します。隣に作られる.profile.jsonと一緒に保存してください。\n\n_EDCBX_DIRECT_によるFilePath環境変数の受け渡しに対応したEDCB版が必要です。","わかりました");
  },true),Pixel.Text("BATには作成時の設定を固定します。設定を変えた場合はBATを作り直してください。\n対応：EDCBXの環境変数によるBAT直接実行。",12,Pixel.Muted)),Color.FromArgb("#E8F0FE"));
  var folder=Pixel.Entry(s.WatchFolder,"録画TSが保存されるフォルダー");
  var watcher=new Switch {IsToggled=s.WatchEnabled};
  var quiet=Pixel.Entry(s.QuietSeconds.ToString());quiet.Keyboard=Keyboard.Numeric;
  var recurse=new Switch {IsToggled=s.Recursive};
  var parallel=new Stepper {Minimum=1,Maximum=8,Increment=1,Value=s.Parallel};var parallelLabel=Pixel.Text(s.Parallel+"本",22,null,true);parallel.ValueChanged+=(a,e)=>parallelLabel.Text=(int)e.NewValue+"本";
  var browse=Pixel.Button("フォルダーを選択",async()=>{string? path=await Pickers.Folder();if(path!=null)folder.Text=path;});
  var watch=Pixel.Card(Pixel.Stack(Pixel.Text("フォルダー監視でも自動化",22,null,true),Pixel.Text("録画中のTSは、更新が止まり書き込みが終わるまで待ちます。\n確実な録画終了通知には、上のBAT連携をおすすめします。",14,Pixel.Muted),Pixel.Field("録画フォルダー",folder),browse,Pixel.Row(Pixel.Text("サブフォルダーも監視",14),recurse),Pixel.Field("更新が止まってから待つ秒数",quiet),Pixel.Row(Pixel.Text("監視を有効にする",16,null,true),watcher)));
  var save=Pixel.Button("自動処理の設定を保存",()=> {
   if(!int.TryParse(quiet.Text,out int seconds))throw new ArgumentException("待機秒数は整数で入力してください。");
   if(watcher.IsToggled&&!Directory.Exists(folder.Text))throw new ArgumentException("録画フォルダーを選択してください。");
   s.WatchFolder=folder.Text??"";s.WatchEnabled=watcher.IsToggled;s.Recursive=recurse.IsToggled;s.QuietSeconds=seconds;s.Parallel=(int)parallel.Value;client.Save();client.Wake();notice("自動処理の設定を保存しました。");
  },true);
  Content=Pixel.Scroll(Pixel.Stack(bat,watch,Pixel.Card(Pixel.Stack(Pixel.Text("一度に変換する本数",22,null,true),Pixel.Row(parallelLabel,parallel),Pixel.Text("GPU・CPUの負荷やストレージ速度に合わせて調整してください。",13,Pixel.Muted))),save));
 }
}
