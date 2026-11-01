using TsEncodeFlow;
namespace EncodeFlow.App;
sealed class StationLogosView:ContentView {
 readonly EngineClient client;readonly CollectionView list;readonly Label summary=Pixel.Text("読込み中…",14,Pixel.Muted);List<StationLogo> stations=[];int refreshRevision;
 sealed record Row(StationLogo Station,string? Path,string? Fingerprint,bool Current,Job? Review,Job? Configure);
 public StationLogosView(EngineClient client){this.client=client;
  var header=Pixel.Stack(Pixel.Text("局ロゴを画像で確認・管理",24,null,true),Pixel.Text("市松模様は透過確認用です。手動追加はLGD／LGD2に対応します。使用中の版を削除すると残っている最新の版へ切り替わり、最後の版を削除すると未登録になります。",14,Pixel.Muted),Pixel.Row(Pixel.Button("LGDを手動追加",async()=>{await LogoImportPage.Open(client,stations);await Refresh();},true),Pixel.Button("一覧を更新",Refresh),Pixel.Button("保存フォルダーを開く",()=>{string folder=System.IO.Path.Combine(client.Data,"Logos");Directory.CreateDirectory(folder);Pickers.Open(folder);})),summary);
  list=new CollectionView {SelectionMode=SelectionMode.None,ItemSizingStrategy=ItemSizingStrategy.MeasureFirstItem,ItemTemplate=new DataTemplate(()=>new Card(this)),EmptyView=Pixel.Text("TSを追加すると放送局が表示されます。手動追加から新しい放送局を登録することもできます。",14,Pixel.Muted)};
  var grid=new Grid {RowDefinitions=new RowDefinitionCollection {new(GridLength.Auto),new(GridLength.Star)},RowSpacing=16};grid.Add(header,0,0);grid.Add(list,0,1);Content=grid;
  Loaded+=async(s,e)=>{try{await Refresh();}catch(Exception ex){summary.Text=ex.Message;Pixel.LogError("ロゴ一覧",ex);}};
 }
 async Task Refresh(){int ticket=++refreshRevision;client.Refresh();var jobs=client.Jobs.ToList();var result=await Task.Run(()=>{
   var known=StationLogos.List(client.Data).ToDictionary(s=>s.Key,StringComparer.Ordinal);
   foreach(var j in jobs.Where(j=>j.Metadata!=null&&j.Metadata.Width>=8&&j.Metadata.Height>=8)) {string key=StationLogos.Key(j);if(!known.ContainsKey(key))known[key]=StationLogos.Read(client.Data,j);else if(string.IsNullOrWhiteSpace(known[key].Channel)||known[key].Channel.StartsWith("局ID："))known[key].Channel=j.Metadata.Channel;}
   var entries=known.Values.OrderBy(s=>s.Channel).ToList();var rows=new List<Row>();
   foreach(var station in entries){var related=jobs.Where(j=>j.Metadata!=null&&StationLogos.Key(j)==station.Key).ToList();string? current=StationLogos.Find(client.Data,station.Key);var files=StationLogos.Files(client.Data,station.Key);
    foreach(var file in files)rows.Add(new(station,file,Store.Fingerprint(file),string.Equals(file,current,StringComparison.OrdinalIgnoreCase),null,null));
    foreach(var review in related.Where(j=>j.State==State.NeedsLogoReview))rows.Add(new(station,review.LogoCandidate,File.Exists(review.LogoCandidate)?Store.Fingerprint(review.LogoCandidate):null,false,review,null));
    var configure=related.FirstOrDefault(j=>StationLogos.CanConfigure(client.Data,j,client.Settings));if(configure!=null||files.Count==0&&!related.Any(j=>j.State==State.NeedsLogoReview))rows.Add(new(station,null,null,false,null,configure));
   }
   return (entries,rows);
  });if(ticket!=refreshRevision)return;stations=result.entries;list.ItemsSource=result.rows;summary.Text=stations.Count+"局・解像度 / 登録済み "+result.rows.Count(r=>r.Fingerprint!=null&&r.Review==null)+"版 / 生成画像の確認待ち "+result.rows.Count(r=>r.Review!=null)+"件";
 }
 sealed class Card:ContentView {
  readonly StationLogosView owner;readonly Label title=Pixel.Text("",18,null,true),detail=Pixel.Text("",12,Pixel.Muted),status=Pixel.Text("",14,Pixel.Blue,true),imageInfo=Pixel.Text("",12,Pixel.Muted);readonly ContentView image=new(){HeightRequest=110};
  readonly Button remove,enlarge,review,configure;int revision;
  public Card(StationLogosView owner){this.owner=owner;title.MaxLines=2;detail.MaxLines=1;imageInfo.MaxLines=2;
   remove=Pixel.Button("この版を削除",Delete);enlarge=Pixel.Button("画像を拡大",Enlarge);
   review=Pixel.Button("生成ロゴを確認",async()=>{if(BindingContext is Row r&&r.Review!=null){await LogoReviewPage.Open(owner.client,r.Review);await owner.Refresh();}},true);
   configure=Pixel.Button("ロゴ範囲を指定",async()=>{if(BindingContext is Row r&&r.Configure!=null){await LogoAreaPage.Open(owner.client,r.Configure);await owner.Refresh();}},true);
   var import=Pixel.Button("この局に手動追加",async()=>{if(BindingContext is Row r){await LogoImportPage.Open(owner.client,owner.stations,r.Station.Key);await owner.Refresh();}});
   var contents=Pixel.Stack(title,detail,status,image,imageInfo,Pixel.Row(enlarge,import,remove),review,configure);contents.Spacing=8;Content=Pixel.Card(contents,padding:20);HeightRequest=420;Margin=new Thickness(0,0,0,12);
   BindingContextChanged+=async(s,e)=>await LoadPreview();
  }
  async Task LoadPreview(){int ticket=++revision;image.Content=null;if(BindingContext is not Row row)return;
   title.Text=(string.IsNullOrWhiteSpace(row.Station.Channel)?row.Station.Key:row.Station.Channel)+$" · {row.Station.Width} × {row.Station.Height}";
   detail.Text=row.Path==null?row.Station.Key:System.IO.Path.GetFileName(row.Path);status.Text=row.Review!=null?"未承認の生成候補":row.Path==null?"ロゴ未登録":row.Current?"使用中":"保管中（旧版）";
   remove.IsVisible=row.Fingerprint!=null;remove.Text=row.Review!=null?"候補を削除":"この版を削除";enlarge.IsVisible=row.Path!=null;review.IsVisible=row.Review!=null;configure.IsVisible=row.Configure!=null;
   imageInfo.Text=row.Path==null?"ロゴファイルを手動追加するか、TSから範囲を指定して生成できます。":"画像を読み込み中…";
   if(row.Path==null)return;
   try{var bitmap=await Task.Run(()=>LogoCandidates.Read(row.Path,StationLogos.Geometry(row.Station)));if(ticket!=revision||!ReferenceEquals(BindingContext,row))return;image.Content=new LogoImageView(bitmap,110);imageInfo.Text=$"位置 {bitmap.X}, {bitmap.Y} · ロゴ {bitmap.Width} × {bitmap.Height}（輝度・透過表示）";}
   catch(Exception ex){if(ticket==revision){imageInfo.Text="プレビューできません："+ex.Message;image.Content=Pixel.Text("画像を読み込めません",14,Pixel.Muted);}}
  }
  async Task Delete(){if(BindingContext is not Row row||row.Path==null||row.Fingerprint==null)return;var page=Application.Current!.Windows[0].Page!;
   string message=(row.Station.Channel??row.Station.Key)+"\n"+System.IO.Path.GetFileName(row.Path)+"\n\n"+(row.Review!=null?"未承認の生成候補を削除し、範囲指定待ちに戻します。":row.Current?"使用中の版です。残っている最新の版へ切り替えます。最後の版なら未登録になります。":"この版のロゴファイルを削除します。使用中の版は維持します。");
   if(!await page.DisplayAlert("ロゴを削除",message,"削除する","戻る"))return;
   var result=await owner.client.ManageLogo(new QueueManager.Request {Action=row.Review!=null?"delete-logo-candidate":"delete-logo",LogoStation=row.Station,JobId=row.Review?.Id,LogoFileName=System.IO.Path.GetFileName(row.Path),LogoSourceFingerprint=row.Fingerprint,Fingerprint=row.Review?.Fingerprint??row.Fingerprint});await owner.Refresh();owner.summary.Text=result.Message;
  }
  async Task Enlarge(){if(BindingContext is not Row row||row.Path==null)return;var bitmap=await Task.Run(()=>LogoCandidates.Read(row.Path,StationLogos.Geometry(row.Station)));var page=new ContentPage {Title="ロゴ画像",Padding=24};var grid=new Grid {RowDefinitions=new RowDefinitionCollection {new(GridLength.Star),new(GridLength.Auto)},RowSpacing=16};grid.Add(new LogoImageView(bitmap,0),0,0);grid.Add(Pixel.Stack(Pixel.Text((row.Station.Channel??row.Station.Key)+" · "+System.IO.Path.GetFileName(row.Path),18,null,true),Pixel.Button("閉じる",async()=>await page.Navigation.PopModalAsync())),0,1);page.Content=grid;await Application.Current!.Windows[0].Page!.Navigation.PushModalAsync(new NavigationPage(page));}
 }
}
