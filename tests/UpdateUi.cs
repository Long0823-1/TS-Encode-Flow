using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;
static partial class Tests {
 sealed class BlockedUiContext:SynchronizationContext {public int Posted;public override void Post(SendOrPostCallback action,object state){Interlocked.Increment(ref Posted);}}
 static void OnBlockedUi(Action action,string name) {
  Exception error=null;var context=new BlockedUiContext();var thread=new Thread(()=>{SynchronizationContext.SetSynchronizationContext(context);try{action();}catch(Exception e){error=e;}}){IsBackground=true};thread.Start();
  Check(thread.Join(TimeSpan.FromSeconds(8)),name+" completes without waiting for the blocked UI context");if(error!=null)throw error;
  Check(context.Posted==0,name+" does not capture the UI context");
 }
 sealed class UpdateZipHandler:HttpMessageHandler {
  public byte[] Zip;public string Json;public bool OnUi;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){OnUi|=SynchronizationContext.Current is BlockedUiContext;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=request.RequestUri.AbsolutePath.EndsWith(".zip")?new ByteArrayContent(Zip):new StringContent(Json)});}
 }
 static void UpdateUi(string root) {
  string file=Path.Combine(root,"ui-hash.bin");File.WriteAllBytes(file,new byte[16*1024*1024]);string digest;using(var input=File.OpenRead(file))digest="sha256:"+Convert.ToHexString(SHA256.HashData(input));
  OnBlockedUi(()=>SelfUpdate.Verify(file,digest,CancellationToken.None).GetAwaiter().GetResult(),"SHA verification");
  byte[] zip;using(var memory=new MemoryStream()){using(var archive=new ZipArchive(memory,ZipArchiveMode.Create,true)){foreach(string path in new[]{"TS-Encode-Flow.exe","TS-Encode-Flow.dll","Worker/TS-Encode-Worker.exe"}){using var writer=new StreamWriter(archive.CreateEntry(path).Open());writer.Write("fixture");}}zip=memory.ToArray();}
  string sha="sha256:"+Convert.ToHexString(SHA256.HashData(zip));string url="https://github.com/Long0823-1/TS-Encode-Flow/releases/download/v0.6.17/TS-Encode-Flow-0.6.17-win-x64.zip";
  using var handler=new UpdateZipHandler {Zip=zip,Json="[{\"tag_name\":\"v0.6.17\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"TS-Encode-Flow-0.6.17-win-x64.zip\",\"browser_download_url\":\""+url+"\",\"digest\":\""+sha+"\"}]}]"};using var http=new HttpClient(handler);
  var progress=new List<string>();string data=Path.Combine(root,"UiUpdater");UpdateState state=null;
  OnBlockedUi(()=>state=SelfUpdate.DownloadLatest(data,new Version(0,6,16),CancellationToken.None,text=>progress.Add(text),http).GetAwaiter().GetResult(),"download, verify and extraction");
  Check(state.Phase=="staged"&&!handler.OnUi,"update download and extraction run off the UI context");
  Check(progress.Exists(x=>x.Contains("SHA-256"))&&progress.Exists(x=>x.Contains("展開中："))&&progress.Exists(x=>x.Contains("確認中")),"updater reports distinct verification, extraction counts and validation phases");
  OnBlockedUi(()=>Check(!SelfUpdate.ApplyPendingAsync(Path.Combine(root,"NoPendingUpdate"),root).GetAwaiter().GetResult(),"no pending update is a no-op"),"apply preparation");
 }
}
