using System.Runtime.InteropServices;
using TsEncodeFlow;

namespace EncodeFlow.App;
public partial class App:Application {
 GuiBridge? bridge;
 [DllImport("user32.dll")]static extern bool ShowWindow(nint handle,int command);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(nint handle);
 public App(){InitializeComponent();UserAppTheme=AppTheme.Light;}
 protected override Window CreateWindow(IActivationState? state) {
  var page=new MainPage();var nav=new NavigationPage(page);NavigationPage.SetHasNavigationBar(page,false);
  var window=new Window(nav){Title="TS Encode Flow",Width=1420,Height=930,MinimumWidth=1040,MinimumHeight=740};
  var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--data");string data=index>=0&&index+1<args.Length?Path.GetFullPath(args[index+1]):Integration.DataFolder;
  window.Created+=(s,e)=>bridge=new GuiBridge(data,target=>Dispatcher.Dispatch(async()=>{
   try {
    await nav.PopToRootAsync(false);page.OpenSection(target);
    var native=(Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!;var handle=WinRT.Interop.WindowNative.GetWindowHandle(native);ShowWindow(handle,9);native.Activate();SetForegroundWindow(handle);
   }catch(Exception error){Pixel.LogError("GUIを表示",error);}
  }));
  window.Destroying+=(s,e)=>bridge?.Dispose();return window;
 }
}
