#include <windows.h>
#include <shobjidl.h>
#include <wrl/client.h>
#include <string>
#include <vector>
#include <cstdio>
#include <iostream>
using Microsoft::WRL::ComPtr;
static void check(HRESULT hr){if(FAILED(hr))throw hr;}
static std::string utf8(const std::wstring& value){int n=WideCharToMultiByte(CP_UTF8,0,value.c_str(),(int)value.size(),nullptr,0,nullptr,nullptr);std::string out(n,0);WideCharToMultiByte(CP_UTF8,0,value.c_str(),(int)value.size(),out.data(),n,nullptr,nullptr);return out;}
static std::string quoted(const std::wstring& value){std::string out="\"";for(unsigned char c:utf8(value)){if(c=='"'||c=='\\'){out+='\\';out+=c;}else if(c<32){char b[8];sprintf_s(b,"\\u%04x",c);out+=b;}else out+=c;}return out+'"';}
static std::wstring path(IShellItem* item){PWSTR value=nullptr;check(item->GetDisplayName(SIGDN_FILESYSPATH,&value));std::wstring result=value;CoTaskMemFree(value);return result;}
int wmain(int argc,wchar_t** argv){
 HRESULT initialized=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED|COINIT_DISABLE_OLE1DDE);if(FAILED(initialized))return 1;
 int result=0;
 try {
  if(argc==2&&wcscmp(argv[1],L"--probe")==0){ComPtr<IFileOpenDialog> open;ComPtr<IFileSaveDialog> save;check(CoCreateInstance(CLSID_FileOpenDialog,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&open)));check(CoCreateInstance(CLSID_FileSaveDialog,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&save)));std::cout<<"{\"Open\":true,\"Save\":true,\"Folder\":true}";}
  else {
   if(argc!=5)throw E_INVALIDARG;std::wstring mode=argv[1],ext=argv[3],name=argv[4];HWND owner=(HWND)(uintptr_t)_wcstoui64(argv[2],nullptr,10);
   ComPtr<IFileDialog> dialog;ComPtr<IFileOpenDialog> open;ComPtr<IFileSaveDialog> save;
   if(mode==L"save"){check(CoCreateInstance(CLSID_FileSaveDialog,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&save)));check(save.As(&dialog));}
   else{check(CoCreateInstance(CLSID_FileOpenDialog,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&open)));check(open.As(&dialog));}
   DWORD flags=FOS_FORCEFILESYSTEM|FOS_PATHMUSTEXIST|FOS_NOCHANGEDIR;
   if(mode==L"folder")flags|=FOS_PICKFOLDERS;else if(mode==L"save")flags|=FOS_OVERWRITEPROMPT;else flags|=FOS_FILEMUSTEXIST|FOS_ALLOWMULTISELECT;
   check(dialog->SetOptions(flags));
   if(mode!=L"folder") {
    std::wstring pattern;size_t begin=0;while(begin<=ext.size()){size_t end=ext.find(L';',begin);if(end==std::wstring::npos)end=ext.size();if(!pattern.empty())pattern+=L';';pattern+=L'*'+ext.substr(begin,end-begin);if(end==ext.size())break;begin=end+1;}
    COMDLG_FILTERSPEC filters[]={{L"対応ファイル",pattern.c_str()},{L"すべてのファイル",L"*.*"}};check(dialog->SetFileTypes(2,filters));check(dialog->SetFileTypeIndex(1));
    if(mode==L"save"){check(dialog->SetFileName(name.c_str()));check(dialog->SetDefaultExtension(ext.size()>0&&ext[0]==L'.'?ext.c_str()+1:ext.c_str()));}
   }
   check(dialog->SetTitle(mode==L"save"?L"保存先を選択":mode==L"folder"?L"フォルダーを選択":L"ファイルを選択"));
   HRESULT hr=dialog->Show(owner);if(hr==HRESULT_FROM_WIN32(ERROR_CANCELLED)){std::cout<<"{\"Cancelled\":true,\"Paths\":[]}";}
   else {
    check(hr);std::vector<std::wstring> paths;
    if(mode==L"open"){ComPtr<IShellItemArray> items;check(open->GetResults(&items));DWORD count=0;check(items->GetCount(&count));for(DWORD i=0;i<count;i++){ComPtr<IShellItem> item;check(items->GetItemAt(i,&item));paths.push_back(path(item.Get()));}}
    else{ComPtr<IShellItem> item;check(dialog->GetResult(&item));paths.push_back(path(item.Get()));}
    std::cout<<"{\"Cancelled\":false,\"Paths\":[";bool first=true;for(const auto& p:paths){if(!first)std::cout<<',';first=false;std::cout<<quoted(p);}std::cout<<"]}";
   }
  }
 }catch(HRESULT hr){fprintf(stderr,"Windows file dialog failed: HRESULT 0x%08lx\n",(unsigned long)hr);result=1;}
 CoUninitialize();return result;
}
