namespace EncodeFlow.App;
public partial class App:Application {
 public App(){InitializeComponent();UserAppTheme=AppTheme.Light;}
 protected override Window CreateWindow(IActivationState? state) {
  var page=new MainPage();var nav=new NavigationPage(page);NavigationPage.SetHasNavigationBar(page,false);
  return new Window(nav){Title="TS Encode Flow",Width=1420,Height=930,MinimumWidth=1040,MinimumHeight=740};
 }
}
