using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class UpdateResult {
  public bool Available;
  public string Version="",Message="";
 }
 public static class Updates {
  public const string ReleasesUrl="https://github.com/Long0823-1/TS-Encode-Flow/releases/latest";
  public const string ApiUrl="https://api.github.com/repos/Long0823-1/TS-Encode-Flow/releases/latest";
  public static async Task<UpdateResult> Check(HttpClient http,Version current,CancellationToken cancellation=default) {
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(12));
   try {
    using var request=new HttpRequestMessage(HttpMethod.Get,ApiUrl);
    request.Headers.UserAgent.ParseAdd("TS-Encode-Flow/"+current.ToString(3));
    request.Headers.Accept.ParseAdd("application/vnd.github+json");
    using var response=await http.SendAsync(request,timeout.Token).ConfigureAwait(false);
    if(response.StatusCode==HttpStatusCode.NotFound)return new UpdateResult {Message="更新リリースはまだありません。"};
    if(response.StatusCode==HttpStatusCode.Forbidden||(int)response.StatusCode==429)return new UpdateResult {Message="GitHubの利用制限により確認できません。時間をおいて再確認してください。"};
    response.EnsureSuccessStatusCode();
    using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));var root=json.RootElement;
    if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())return new UpdateResult {Message="新しい正式版はありません。"};
    string tag=root.GetProperty("tag_name").GetString()??"";
    if(!Version.TryParse(tag.TrimStart('v','V'),out var latest))return new UpdateResult {Message="リリースのバージョンを読み取れませんでした。"};
    var normalized=new Version(current.Major,current.Minor,Math.Max(0,current.Build));
    bool newer=latest>normalized;
    return new UpdateResult {Available=newer,Version=tag,Message=newer?tag+" が利用できます。更新内容を確認して最新版へ更新してください。":"最新版を使用しています（"+normalized.ToString(3)+"）。"};
   }catch(OperationCanceledException){return new UpdateResult {Message="更新確認がタイムアウト、または中止されました。後で再確認できます。"};}
   catch(HttpRequestException){return new UpdateResult {Message="GitHubに接続できませんでした。通信状態を確認して再確認してください。"};}
   catch(Exception e)when(e is JsonException||e is InvalidOperationException||e is System.Collections.Generic.KeyNotFoundException){return new UpdateResult {Message="更新情報を読み取れませんでした。後で再確認できます。"};}
  }
 }
}
