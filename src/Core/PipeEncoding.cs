using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public static class PipeEncoding {
  static Process Create(Command c,bool producer)=>new Process {StartInfo=new ProcessStartInfo(c.Exe,string.Join(" ",c.Args.Select(Processes.Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=!producer,WorkingDirectory=Path.GetDirectoryName(c.Exe)}};
  static async Task<string> Drain(StreamReader reader,Action<string> log) {var b=new StringBuilder();string line;while((line=await reader.ReadLineAsync())!=null){log?.Invoke(line);if(b.Length<24000)b.AppendLine(line);}return b.ToString();}
  public static async Task Run(Command producer,Command consumer,CancellationToken token,Action<string> log) {
   if(!File.Exists(producer.Exe)||!File.Exists(consumer.Exe))throw new FileNotFoundException("パイプ用の変換ツールがありません。");
   token.ThrowIfCancellationRequested();using var source=Create(producer,true);using var encoder=Create(consumer,false);using var children=new ChildJob();
   bool startedSource=false,startedEncoder=false;
   try {
    encoder.Start();startedEncoder=true;children.Assign(encoder);source.Start();startedSource=true;children.Assign(source);
    using var cancel=token.Register(()=>children.Kill());
    var sourceLog=Drain(source.StandardError,log);var encoderLog=Drain(encoder.StandardError,log);var output=Drain(encoder.StandardOutput,log);
    Exception copyError=null;
    try{await source.StandardOutput.BaseStream.CopyToAsync(encoder.StandardInput.BaseStream,token);}catch(Exception e)when(e is IOException||e is OperationCanceledException){copyError=e;children.Kill();}
    finally{try{encoder.StandardInput.Close();}catch(IOException){}}
    await Task.WhenAll(source.WaitForExitAsync(),encoder.WaitForExitAsync());string se=await sourceLog,ee=await encoderLog;await output;token.ThrowIfCancellationRequested();
    if(copyError!=null||source.ExitCode!=0||encoder.ExitCode!=0)throw new InvalidDataException("パイプ変換に失敗：FFmpeg="+source.ExitCode+" / GPU="+encoder.ExitCode+"\n"+Converter.Tail(se+ee),copyError);
   }finally{if(startedSource||startedEncoder)children.Kill();}
  }
 }
}
