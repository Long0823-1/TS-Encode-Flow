using TsEncodeFlow;
namespace EncodeFlow.App;
public static class MauiProgram {
 public static MauiApp CreateMauiApp() {
  var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--data"),page=Array.IndexOf(args,"--page");
  string data=index>=0&&index+1<args.Length?Path.GetFullPath(args[index+1]):Integration.DataFolder;
  string target=page>=0&&page+1<args.Length?args[page+1]:"ダッシュボード";
  if(GuiBridge.TryOpen(data,target).GetAwaiter().GetResult())Environment.Exit(0);
  return MauiApp.CreateBuilder().UseMauiApp<App>().Build();
 }
}
