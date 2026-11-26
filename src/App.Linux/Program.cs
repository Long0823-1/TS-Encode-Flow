using Avalonia;
using Avalonia.Headless;
using TsEncodeFlow;

namespace EncodeFlow.App.Linux;

static class Program {
 [STAThread]
 public static int Main(string[] args) {
  // 診断モード（GUIを起動せず、GUIと同じクライアント経路だけを検証する）
  int smoke=Array.IndexOf(args,"--smoke-enqueue");
  if(smoke>=0)return SmokeEnqueue(args,smoke);
  int b2a=Array.IndexOf(args,"--smoke-b2a");
  if(b2a>=0)return SmokeB2a(args,b2a);
  int shot=Array.IndexOf(args,"--screenshot");
  if(shot>=0&&shot+1<args.Length)return Screenshot(args,args[shot+1]);
  BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
  return 0;
 }

 public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

 // --smoke-enqueue <TSファイル> [エンジン名]
 // EngineClientLinux.Add（＝GUIの「TSを追加」と同じ経路）で登録し、ワーカーの処理結果を queue.json から読み戻す。
 static int SmokeEnqueue(string[] args,int index) {
  if(index+1>=args.Length){Console.Error.WriteLine("TSファイルを指定してください。");return 2;}
  string file=Path.GetFullPath(args[index+1]);
  string engine=index+2<args.Length?args[index+2]:"";
  try {
   var client=new EngineClientLinux();
   if(engine.Length>0) {
    var preset=client.Settings.Presets.FirstOrDefault(p=>PresetCatalog.EngineName(p.Engine)==engine&&p.Codec==Codec.H264)
     ??client.Settings.Presets.FirstOrDefault(p=>PresetCatalog.EngineName(p.Engine)==engine);
    if(preset==null)throw new ArgumentException("そのエンジンのプリセットがありません："+engine);
    client.Choose(preset);
   }
   Console.WriteLine("データフォルダー："+client.Data);
   Console.WriteLine("使用プリセット："+client.Selected.Name+"（"+PresetCatalog.EngineName(client.Selected.Engine)+" · "+client.Selected.Codec+" · "+client.Selected.Container+"）");
   int added=client.Add([file]);
   Console.WriteLine(added+"本を登録しました。ワーカーの処理を待ちます…");
   for(int i=0;i<180;i++) {
    Thread.Sleep(1000);client.Refresh();
    var job=client.Jobs.FirstOrDefault(j=>j.Input==file);
    if(job==null)continue;
    Console.WriteLine("状態："+job.State+" "+job.Progress.ToString("0")+"% "+job.Message);
    if(job.State==State.Completed){Console.WriteLine("出力："+job.Output);return 0;}
    if(job.State==State.Failed){Console.Error.WriteLine("失敗："+job.Message);return 1;}
   }
   Console.Error.WriteLine("タイムアウト：変換の完了を確認できませんでした。");
   return 3;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }

 // --smoke-b2a <TSファイル> : B-2aの検証。プリセット編集モデル→SavePreset、設定画面の保存経路、
 // 自動化画面の保存経路を実際に通し、最後に登録したジョブへワーカーが反映するかを確認する。
 static int SmokeB2a(string[] args,int index) {
  if(index+1>=args.Length){Console.Error.WriteLine("TSファイルを指定してください。");return 2;}
  string file=Path.GetFullPath(args[index+1]);
  try {
   var client=new EngineClientLinux();
   Console.WriteLine("データフォルダー："+client.Data);
   // 1) プリセット：編集モデル経由で VA-API のカスタムプリセットを作成・保存（選択中になる）
   var model=new PresetEditorModel(client.Selected,true) {Name="B2aスモーク用"};
   model.EngineIndex=(int)Engine.Vaapi;model.CodecIndex=(int)Codec.H264;
   client.SavePreset(model.Apply());
   var saved=client.Settings.Presets.FirstOrDefault(p=>p.Name=="B2aスモーク用"&&string.IsNullOrEmpty(p.FactoryId));
   if(saved==null)throw new InvalidOperationException("カスタムプリセットが保存されていません。");
   if(!ReferenceEquals(client.Selected,saved))throw new InvalidOperationException("保存したプリセットが選択中になっていません。");
   Console.WriteLine("プリセット保存OK："+saved.Name+"（"+PresetCatalog.EngineName(saved.Engine)+" · "+saved.Codec+"）");
   // 2) 設定：設定画面の保存経路で出力先と同時変換数を変更し、settings.json へ反映されるか
   string outDir=Path.Combine(client.Data,"smoke-output");
   var values=SettingsValues.Capture(client.Settings);values.OutputFolder=outDir;values.Parallel=2;
   SettingsScreenLogic.ApplySettings(client,values);
   var disk=Integration.LoadSettings(client.Data);
   if(Path.GetFullPath(disk.OutputFolder)!=Path.GetFullPath(outDir))throw new InvalidOperationException("保存先が settings.json に反映されていません。");
   if(disk.Parallel!=2)throw new InvalidOperationException("同時変換数が settings.json に反映されていません。");
   Console.WriteLine("設定保存OK：出力先="+disk.OutputFolder+" / 同時変換数="+disk.Parallel);
   // 3) 自動化：自動化画面の保存経路（検証→保存→Wake）を実際に通す
   var automation=AutomationValues.Capture(client.Settings);
   AutomationScreenLogic.ApplyAutomation(client,automation);
   Console.WriteLine("自動化保存OK：監視="+(automation.WatchEnabled?"有効":"無効")+" / 同時変換数="+Integration.LoadSettings(client.Data).Parallel);
   // 4) 登録：ワーカーがカスタムプリセットと新しい出力先で処理するか
   int added=client.Add([file]);
   Console.WriteLine(added+"本を登録しました。ワーカーの処理を待ちます…");
   for(int i=0;i<240;i++) {
    Thread.Sleep(1000);client.Refresh();
    var job=client.Jobs.FirstOrDefault(j=>j.Input==file);
    if(job==null)continue;
    if(job.State==State.Completed) {
     if(job.Preset.Name!="B2aスモーク用")throw new InvalidOperationException("ジョブのプリセットがカスタムプリセットではありません："+job.Preset.Name);
     if(!Path.GetFullPath(job.Output).StartsWith(Path.GetFullPath(outDir)))throw new InvalidOperationException("ジョブの出力先が新しい保存先ではありません："+job.Output);
     Console.WriteLine("変換完了："+job.Output+"（プリセット："+job.Preset.Name+"）");
     Console.WriteLine("SMOKE-B2A OK");
     return 0;
    }
    if(job.State==State.Failed){Console.Error.WriteLine("失敗："+job.Message);return 1;}
   }
   Console.Error.WriteLine("タイムアウト：変換の完了を確認できませんでした。");
   return 3;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }

 // --screenshot <PNGパス> [--section <セクション名>] [--edit-preset] : ヘッドレスで描画して保存する（検証用）
 static int Screenshot(string[] args,string output) {
  output=Path.GetFullPath(output);
  AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).SetupWithoutStarting();
  var viewModel=new MainViewModel(new EngineClientLinux());
  int si=Array.IndexOf(args,"--section");
  if(si>=0&&si+1<args.Length)viewModel.Section=args[si+1];
  if(Array.IndexOf(args,"--edit-preset")>=0)viewModel.OpenPresetEditorDemo();
  var window=new MainWindow {DataContext=viewModel,Width=1180,Height=760};
  window.Show();
  // 自動起動したワーカーの状態書き込みを待ってから撮る
  for(int i=0;i<4;i++){AvaloniaHeadlessPlatform.ForceRenderTimerTick();Thread.Sleep(400);viewModel.Refresh();}
  AvaloniaHeadlessPlatform.ForceRenderTimerTick();
  var frame=window.CaptureRenderedFrame();
  if(frame==null){Console.Error.WriteLine("フレームを取得できませんでした。");return 1;}
  Directory.CreateDirectory(Path.GetDirectoryName(output)!);
  frame.Save(output);
  Console.WriteLine("スクリーンショットを保存しました："+output);
  return 0;
 }
}
