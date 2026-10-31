using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class GuiBridge:IDisposable {
  readonly CancellationTokenSource stop=new();
  static string Key(string data)=>"TS-Encode-Flow-Gui-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(data).TrimEnd('\\').ToUpperInvariant())));
  public GuiBridge(string data,Action<string> activate) {
   _=Task.Run(async()=>{
    while(!stop.IsCancellationRequested)try {
     using var pipe=new NamedPipeServerStream(Key(data),PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
     await pipe.WaitForConnectionAsync(stop.Token);using var reader=new StreamReader(pipe);
     using var readTimeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);readTimeout.CancelAfter(3000);
     string page=await reader.ReadLineAsync(readTimeout.Token);if(page!=null)activate(page);
    }catch(OperationCanceledException){}catch(IOException){try{await Task.Delay(300,stop.Token);}catch(OperationCanceledException){}}
   });
  }
  public static async Task<bool> TryOpen(string data,string page) {
   try {
    using var pipe=new NamedPipeClientStream(".",Key(data),PipeDirection.Out,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
    using var timeout=new CancellationTokenSource(600);await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
    using var writer=new StreamWriter(pipe){AutoFlush=true};await writer.WriteLineAsync(page).ConfigureAwait(false);return true;
   }catch(Exception e)when(e is IOException||e is OperationCanceledException||e is TimeoutException){return false;}
  }
  public void Dispose()=>stop.Cancel();
 }
}
