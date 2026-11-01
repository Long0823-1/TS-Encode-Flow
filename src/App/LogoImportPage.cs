using TsEncodeFlow;
namespace EncodeFlow.App;
sealed class LogoImportPage:ContentPage {
 readonly EngineClient client;readonly List<StationLogo> stations;readonly Picker station=new(){Title="登録先の放送局"};
 readonly Entry channel=Pixel.Entry("","放送局名"),onid=Pixel.Entry("","ONID"),tsid=Pixel.Entry("","TSID"),sid=Pixel.Entry("","SID"),width=Pixel.Entry("1440"),height=Pixel.Entry("1080");
 readonly Label info=Pixel.Text("LGDファイルを選ぶと画像を表示します。",14,Pixel.Muted);readonly ContentView preview=new();readonly Button add;readonly View newFields;
 string? source,fingerprint;StationLogo? selected;int revision;
 public static async Task Open(EngineClient client,List<StationLogo> stations,string? key=null){var page=new LogoImportPage(client,stations,key);var done=new TaskCompletionSource();page.Unloaded+=(s,e)=>done.TrySetResult();await Application.Current!.Windows[0].Page!.Navigation.PushModalAsync(new NavigationPage(page));await done.Task;}
 LogoImportPage(EngineClient client,List<StationLogo> stations,string? key){
  this.client=client;this.stations=stations;Title="局ロゴを手動追加";Padding=24;
  station.ItemsSource=stations.Select(s=>(s.Channel??s.Key)+" · "+s.Width+" × "+s.Height+" · "+s.Key).Append("新しい放送局を指定する").ToList();
  newFields=Pixel.Stack(Pixel.Field("放送局名",channel),Pixel.Row(Pixel.Field("ONID",onid),Pixel.Field("TSID",tsid),Pixel.Field("SID",sid)),Pixel.Row(Pixel.Field("元映像の幅",width),Pixel.Field("元映像の高さ",height)));
  foreach(var e in new[]{onid,tsid,sid,width,height}){e.Keyboard=Keyboard.Numeric;e.WidthRequest=130;}
  add=Pixel.Button("この局のロゴとして追加して再開",Save,true);add.IsEnabled=false;
  void Invalidate(){revision++;selected=null;fingerprint=null;add.IsEnabled=false;preview.Content=null;info.Text=source==null?"LGDファイルを選ぶと画像を表示します。":"登録先を変更しました。「プレビューを表示」で確認してください。";}
  station.SelectedIndexChanged+=(s,e)=>{newFields.IsVisible=station.SelectedIndex==stations.Count;Invalidate();};
  foreach(var entry in new[]{channel,onid,tsid,sid,width,height})entry.TextChanged+=(s,e)=>Invalidate();
  station.SelectedIndex=key==null?(stations.Count>0?0:stations.Count):Math.Max(0,stations.FindIndex(s=>s.Key==key));newFields.IsVisible=station.SelectedIndex==stations.Count;
  Content=Pixel.Scroll(Pixel.Stack(Pixel.Text("放送局を選んでロゴを追加",24,null,true),Pixel.Field("登録先",station),newFields,Pixel.Text("LGDには元映像全体の解像度が保存されていません。録画と同じ元解像度を選んでください。追加した版を使用中にし、以前の版も保持します。PNG・JPEGではなく、位置と透過情報を含むLGD／LGD2を指定します。",14,Pixel.Muted),Pixel.Row(Pixel.Button("LGDを選ぶ",async()=>{var paths=await Pickers.Files(".lgd;.lgd2");if(paths.Count==0)return;if(paths.Count!=1)throw new ArgumentException("LGDは1件ずつ選択してください。");source=paths[0];await Load();}),Pixel.Button("プレビューを表示",Load)),info,preview,Pixel.Row(add,Pixel.Button("閉じる",async()=>await Navigation.PopModalAsync()))));
 }
 StationLogo Target(){if(station.SelectedIndex>=0&&station.SelectedIndex<stations.Count)return Store.Clone(stations[station.SelectedIndex]);
  int Number(Entry entry,int min,int max){if(!int.TryParse(entry.Text,out int value)||value<min||value>max)throw new ArgumentException($"IDは1～65535、元解像度は8～16384で入力してください。");return value;}
  int n=Number(onid,1,65535),t=Number(tsid,1,65535),s=Number(sid,1,65535),w=Number(width,8,16384),h=Number(height,8,16384);return new StationLogo {Key=$"{n}-{t}-{s}-{w}x{h}",Channel=channel.Text??"",Width=w,Height=h};
 }
 async Task Load(){if(source==null)throw new ArgumentException("LGDファイルを選んでください。");int ticket=++revision;add.IsEnabled=false;selected=null;var target=Target();string path=source,identity=Store.Fingerprint(path);info.Text="ロゴ画像を読み込み中…";
  try{var bitmap=await Task.Run(()=>LogoCandidates.Read(path,StationLogos.Geometry(target)));if(ticket!=revision)return;if(Store.Fingerprint(path)!=identity)throw new IOException("LGDが変更されました。選び直してください。");preview.Content=new LogoImageView(bitmap,180);info.Text=Path.GetFileName(path)+$" · 位置 {bitmap.X}, {bitmap.Y} · {bitmap.Width} × {bitmap.Height}";selected=target;fingerprint=identity;add.IsEnabled=true;}catch{if(ticket==revision){preview.Content=null;info.Text="画像を読み込めません。登録先の元解像度とLGD形式を確認してください。";}throw;}
 }
 async Task Save(){if(source==null||fingerprint==null||selected==null)throw new ArgumentException("プレビューを確認してください。");await client.ManageLogo(new QueueManager.Request {Action="import-logo",LogoStation=selected,LogoSource=source,LogoSourceFingerprint=fingerprint});await Navigation.PopModalAsync();}
}
