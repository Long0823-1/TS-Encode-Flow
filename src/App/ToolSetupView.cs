using TsEncodeFlow;

namespace EncodeFlow.App;
sealed class ToolSetupView:ContentView {
 public ToolSetupView(EngineClient client,Action applied) {
  var status=Pixel.Text("公式配布元からWindows x64版を取得し、設定に適用します。合計約800MBのダウンロードと展開用の空き容量が必要です。",13,Pixel.Muted);
  var list=Pixel.Stack();var actions=new List<Button>();Button? all=null;CancellationTokenSource? cancellation=null;
  var cancel=Pixel.Button("ダウンロードを中止",()=>cancellation?.Cancel());cancel.IsEnabled=false;
  async Task Install(IEnumerable<ToolPackage> packages) {
   if(cancellation!=null)return;cancellation=new();cancel.IsEnabled=true;foreach(var b in actions)b.IsEnabled=false;
   int success=0;var errors=new List<string>();
   try {foreach(var tool in packages){cancellation.Token.ThrowIfCancellationRequested();try {
     var receipt=await Task.Run(()=>ToolInstaller.Install(client.Data,tool,cancellation.Token,text=>MainThread.BeginInvokeOnMainThread(()=>status.Text=tool.Name+"："+text)));
     ToolInstaller.Apply(client.Settings,tool,receipt);client.Save();applied();success++;status.Text=tool.Name+" "+receipt.Version+"：適用しました。";
    }catch(OperationCanceledException){throw;}catch(Exception ex){errors.Add(tool.Name+"："+ex.Message);}}
    status.Text=$"{success}件をダウンロード・適用しました。"+(errors.Count==0?"":"\n"+string.Join("\n",errors));
   }catch(OperationCanceledException){status.Text=$"中止しました。適用済みの{success}件は利用できます。";}
   finally{cancellation.Dispose();cancellation=null;cancel.IsEnabled=false;foreach(var b in actions)b.IsEnabled=true;Refresh();}
  }
  all=Pixel.Button("すべてダウンロードして適用",()=>Install(ToolInstaller.Packages),true);actions.Add(all);
  void Refresh() {
   list.Children.Clear();actions.RemoveAll(b=>b!=all);foreach(var tool in ToolInstaller.Packages){var receipt=ToolInstaller.Installed(client.Data,tool);var install=Pixel.Button("ダウンロードして適用",()=>Install(new[]{tool}));actions.Add(install);
    list.Children.Add(Pixel.Card(Pixel.Stack(Pixel.Text(tool.Name,18,null,true),Pixel.Text(File.Exists(receipt.Path)?"導入済み："+receipt.Version:"未ダウンロード",13,Pixel.Muted),Pixel.Text(tool.License,13),install,Pixel.Button("ライセンス・配布元を確認",async()=>{
     var files=ToolInstaller.LicenseFiles(client.Data,tool);var content=Pixel.Stack(Pixel.Text(tool.Name,22,null,true),Pixel.Text(tool.License+"\n"+receipt.LicenseNotice,14),Pixel.Button("配布元・ソースコード",()=>Pickers.Open(tool.Source)),Pixel.Text("取得元："+receipt.Asset+"\nSHA-256："+receipt.Sha256+"\n"+(receipt.UpstreamDigestVerified?"配布元のダイジェストと照合済み":"配布元のダイジェスト照合なし"),12,Pixel.Muted));
     if(tool.Id=="cm-components")foreach(var component in new[]{("chapter_exe · GPL-2.0","https://github.com/nekopanda/chapter_exe"),("join_logo_scp · GPL-2.0","https://github.com/nekopanda/join_logo_scp"),("AviSynth · GPL-2.0-or-later","https://github.com/nekopanda/AviSynthPlus"),("L-SMASH Works · ISC / 依存ライブラリの条件","https://github.com/VFR-maniac/L-SMASH-Works"),("Microsoft Visual C++ランタイム · Microsoft Software License Terms","https://visualstudio.microsoft.com/license-terms/")}) {string url=component.Item2;content.Children.Add(Pixel.Button(component.Item1,()=>Pickers.Open(url)));}
     if(files.Length==0)content.Children.Add(Pixel.Text("ダウンロード後、同梱のライセンス文書をここから開けます。各構成ソフトの条件は配布元でも確認できます。",14));
     foreach(string file in files){string path=file;content.Children.Add(Pixel.Button(Path.GetFileName(path),()=>ShowDocument(path)));}
     var page=new ContentPage {Title="ライセンス",BackgroundColor=Pixel.Surface,Content=Pixel.Scroll(content)};content.Children.Add(Pixel.Button("閉じる",async()=>{await Application.Current!.Windows[0].Page!.Navigation.PopModalAsync();}));await Application.Current!.Windows[0].Page!.Navigation.PushModalAsync(new NavigationPage(page));
    }))));
   }
  }
  Refresh();Content=Pixel.Stack(Pixel.Text("ツールをまとめて準備",22,null,true),status,all,cancel,Pixel.Text("字幕抽出のCaptionProbeはアプリに同梱済みです。GPUドライバーはPCメーカー／GPUメーカーから導入してください。CM解析はロゴ検出・無音／シーン検出・join_logo_scpのみを利用します。Amatsukaze本体は導入しません。ロゴは自動生成、または設定で指定できます。",13,Pixel.Muted),list,Pixel.Button("同梱ツール・アプリのライセンス",ShowBundledLicenses),Pixel.Button("7-Zipのライセンス（展開用に自動取得）",()=>Pickers.Open("https://www.7-zip.org/license.txt")));
 }
 static async Task ShowDocument(string path) {
  System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);byte[] bytes=await File.ReadAllBytesAsync(path);string text;
  try{text=new System.Text.UTF8Encoding(false,true).GetString(bytes);}catch(System.Text.DecoderFallbackException){text=System.Text.Encoding.GetEncoding(932).GetString(bytes);}
  var body=Pixel.Stack(Pixel.Text(Path.GetFileName(path),20,null,true),new Editor {Text=text,IsReadOnly=true,HeightRequest=500,FontSize=13},Pixel.Button("閉じる",async()=>{await Application.Current!.Windows[0].Page!.Navigation.PopModalAsync();}));
  await Application.Current!.Windows[0].Page!.Navigation.PushModalAsync(new NavigationPage(new ContentPage {Title="ライセンス文書",Content=Pixel.Scroll(body)}));
 }
 static async Task ShowBundledLicenses() {
  var content=Pixel.Stack(Pixel.Text("同梱ツール・アプリのライセンス",22,null,true));
  foreach(string file in new[]{"LICENSE","THIRD-PARTY-NOTICES.md","docs/libaribcaption-LICENSE.txt","docs/dotnet-LICENSE.txt","docs/dotnet-ThirdPartyNotices.txt","docs/maui-LICENSE.txt","docs/windows-app-sdk-LICENSE.txt"}){string path=Path.Combine(AppContext.BaseDirectory,file);if(File.Exists(path))content.Children.Add(Pixel.Button(Path.GetFileName(path),()=>ShowDocument(path)));}
  content.Children.Add(Pixel.Button("閉じる",async()=>{await Application.Current!.Windows[0].Page!.Navigation.PopModalAsync();}));await Application.Current!.Windows[0].Page!.Navigation.PushModalAsync(new NavigationPage(new ContentPage {Title="ライセンス",Content=Pixel.Scroll(content)}));
 }
}
