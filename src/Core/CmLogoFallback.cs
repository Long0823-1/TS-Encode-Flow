using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public static class CmLogoFallback {
  public static async Task<bool> UseLogo(Preset preset,bool automatic,Func<Task<bool>> detect,CancellationToken token,Action<string> log) {
   token.ThrowIfCancellationRequested();
   if(preset.CmNoLogo)return false;
   bool success=await detect();
   token.ThrowIfCancellationRequested();
   if(success)return true;
   if(automatic&&preset.CmFallbackWithoutLogo){log("プリセットの設定により、自動ロゴ取得失敗後は無音・シーンチェンジのみでCM解析を続行します。");return false;}
   if(!automatic)throw new InvalidDataException("指定されたロゴLGDでの検出に失敗しました。手動LGDの指定エラーは自動切替の対象外です。ファイルとログを確認してください。");
   throw new InvalidDataException("ロゴ検出に失敗しました。ロゴLGDを指定するか、プリセットで「ロゴ取得失敗時は無音・シーンチェンジのみで続行」を事前にONにしてください。");
  }
 }
}
