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
  int shot=Array.IndexOf(args,"--screenshot");
  if(shot>=0&&shot+1<args.Length)return Screenshot(args[shot+1]);
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

 // --screenshot <PNGパス> : ヘッドレスでメインウィンドウを描画して保存する（検証用）
 static int Screenshot(string output) {
  output=Path.GetFullPath(output);
  AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).SetupWithoutStarting();
  var viewModel=new MainViewModel(new EngineClientLinux());
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
