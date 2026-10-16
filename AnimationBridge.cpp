// Correct only the Search icon animation. Never toggle Search, inject input,
// alter focus, write Search settings, or replace system files.
// Interfaces come from Microsoft WinUI metadata and the documented XAML diagnostics API.
#include <windows.h>
#include <initguid.h>
#include <objidl.h>
#include <ocidl.h>
#include <inspectable.h>
#include <roapi.h>
#include <winstring.h>
#include <xamlom.h>
#include <cstdio>
#include <cstdarg>
#include <map>
#include <mutex>
#include <string>
#include <atomic>
#include <vector>
#include <condition_variable>

static const CLSID CLSID_Bridge = {0x8a92c072,0x259e,0x42d8,{0xb4,0x6a,0x3b,0xfa,0xa1,0xb0,0x0f,0x38}};
static const IID IID_Player = {0xbf7f8865,0x7b79,0x5354,{0x97,0x88,0xc2,0x1e,0x52,0x35,0x91,0xea}};
static const IID IID_Source2 = {0x1a3b53a7,0xa8fe,0x59a1,{0xb5,0x44,0x43,0xa4,0xd9,0xc8,0x1e,0xf2}};
#ifdef BRIDGE_TEST
static const wchar_t* BridgeClass=L"SearchIconStateFix.AnimationBridge.v3-test";
#else
static const wchar_t* BridgeClass=L"SearchIconStateFix.AnimationBridge.v3";
#endif
static UINT RestoreMessage(){static UINT m=RegisterWindowMessageW(L"SearchIconStateFix.RestoreIdleAnimation.v3");return m;}
static UINT TestMessage(){static UINT m=RegisterWindowMessageW(L"SearchIconStateFix.TestAnimationFrame.v3");return m;}
static void RequestDetach();
static UINT DetachMessage(){static UINT m=RegisterWindowMessageW(L"SearchIconStateFix.DetachAnimationBridge.v3");return m;}
static std::mutex logMutex;
static void Log(const wchar_t* format, ...) {
    std::lock_guard<std::mutex> lock(logMutex);
    wchar_t path[32768]; DWORD n=GetEnvironmentVariableW(L"LOCALAPPDATA",path,32700);
    if(!n || n>32000) return;
    wcscat_s(path,L"\\SearchIconStateFix\\animation-bridge.log");
    FILE* file=_wfopen(path,L"a, ccs=UTF-8"); if(!file)return;
    SYSTEMTIME t;GetLocalTime(&t);
    fwprintf(file,L"%04u-%02u-%02u %02u:%02u:%02u thread=%lu ",t.wYear,t.wMonth,t.wDay,t.wHour,t.wMinute,t.wSecond,GetCurrentThreadId());
    va_list args;va_start(args,format);vfwprintf(file,format,args);va_end(args);
    fwprintf(file,L"\n");fclose(file);
}
template<class F> static F Slot(IUnknown* p, unsigned slot) { return reinterpret_cast<F>((*reinterpret_cast<void***>(p))[slot]); }
static std::wstring ClassName(IInspectable* p) {
    HSTRING s=nullptr;std::wstring value;
    if(p && SUCCEEDED(p->GetRuntimeClassName(&s))){UINT32 n=0;const wchar_t* text=WindowsGetStringRawBuffer(s,&n);value.assign(text,n);WindowsDeleteString(s);}
    return value;
}
static int CheckedState(IVisualTreeService* service,InstanceHandle button){
    unsigned sc=0,pc=0;PropertyChainSource* sources=nullptr;PropertyChainValue* values=nullptr;
    HRESULT hr=service->GetPropertyValuesChain(button,&sc,&sources,&pc,&values);int result=-1;
    if(SUCCEEDED(hr))for(unsigned i=0;i<pc;i++){
        auto& p=values[i];if(!p.PropertyName||wcscmp(p.PropertyName,L"IsChecked")||p.Overridden||!p.Value)continue;
        if(!wcscmp(p.Value,L"0")||!_wcsicmp(p.Value,L"False"))result=0;
        else if(!wcscmp(p.Value,L"1")||!_wcsicmp(p.Value,L"True"))result=1;
        break;
    }
    CoTaskMemFree(sources);CoTaskMemFree(values);return result;
}
static HRESULT GetMarker(IUnknown* player,PCWSTR name,double* progress){
    IInspectable* source=nullptr;IUnknown* source2=nullptr;IUnknown* markers=nullptr;HSTRING key=nullptr;
    HRESULT hr=Slot<HRESULT(STDMETHODCALLTYPE*)(IUnknown*,IInspectable**)>(player,8)(player,&source);
    if(SUCCEEDED(hr)&&source && ClassName(source).find(L"AnimatedVisuals.Search")==0){
        hr=source->QueryInterface(IID_Source2,(void**)&source2);
        if(SUCCEEDED(hr))hr=Slot<HRESULT(STDMETHODCALLTYPE*)(IUnknown*,IUnknown**)>(source2,6)(source2,&markers);
        if(SUCCEEDED(hr))hr=WindowsCreateString(name,(UINT32)wcslen(name),&key);
        if(SUCCEEDED(hr))hr=Slot<HRESULT(STDMETHODCALLTYPE*)(IUnknown*,HSTRING,double*)>(markers,6)(markers,key,progress);
    }else hr=E_NOINTERFACE;
    if(key)WindowsDeleteString(key);if(markers)markers->Release();if(source2)source2->Release();if(source)source->Release();return hr;
}
struct Target {
    IXamlDiagnostics* diag;IVisualTreeService* service;InstanceHandle button,icon;
    Target(IXamlDiagnostics* d,IVisualTreeService* s,InstanceHandle b,InstanceHandle i):diag(d),service(s),button(b),icon(i){diag->AddRef();service->AddRef();}
    ~Target(){diag->Release();service->Release();}
    LRESULT Apply(bool simulate){
        if(CheckedState(service,button)!=0){Log(L"Animation restore skipped: Search is active or state unknown");return 0;}
        IInspectable* element=nullptr;IUnknown* player=nullptr;
        HRESULT hr=diag->GetIInspectableFromHandle(icon,&element);
        if(SUCCEEDED(hr)&&element)hr=element->QueryInterface(IID_Player,(void**)&player);
        if(element)element->Release();if(FAILED(hr)||!player)return -1;
        double progress=-1;
        hr=GetMarker(player,simulate?L"NormalOffToNormalOn_End":L"NormalOnToNormalOff_End",&progress);
        if(SUCCEEDED(hr)&&progress>=0&&progress<=1 && CheckedState(service,button)==0)
            hr=Slot<HRESULT(STDMETHODCALLTYPE*)(IUnknown*,double)>(player,24)(player,progress);
        else if(SUCCEEDED(hr))hr=E_ABORT;
        player->Release();Log(L"%s progress=%.9f hr=%08lx; no panel/input/focus operation",simulate?L"Simulated stale frame":L"Restored idle frame",progress,hr);
        return SUCCEEDED(hr)?1:-1;
    }
};
struct Creation { Target* target; bool adopted=false; };
static LRESULT CALLBACK BridgeProc(HWND h,UINT message,WPARAM w,LPARAM l){
    auto target=reinterpret_cast<Target*>(GetWindowLongPtrW(h,GWLP_USERDATA));
    if(message==WM_NCCREATE){auto creation=static_cast<Creation*>(reinterpret_cast<CREATESTRUCTW*>(l)->lpCreateParams);target=creation->target;creation->adopted=true;SetWindowLongPtrW(h,GWLP_USERDATA,(LONG_PTR)target);}
    if(message==DetachMessage()){RequestDetach();return 1;}
    if(target && message==RestoreMessage())return target->Apply(false);
#ifdef BRIDGE_TEST
    if(target && message==TestMessage())return target->Apply(true);
#endif
    if(message==WM_NCDESTROY){SetWindowLongPtrW(h,GWLP_USERDATA,0);delete target;}
    return DefWindowProcW(h,message,w,l);
}
static HWND CreateBridge(IXamlDiagnostics* diag,IVisualTreeService* service,InstanceHandle button,InstanceHandle icon){
    HMODULE module=nullptr;GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(&BridgeProc),&module);
    WNDCLASSW wc{};wc.lpfnWndProc=BridgeProc;wc.hInstance=module;wc.lpszClassName=BridgeClass;
    if(!RegisterClassW(&wc)&&GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)return nullptr;
    auto target=new Target(diag,service,button,icon);
    Creation creation{target};
    HWND h=CreateWindowExW(0,BridgeClass,L"",0,0,0,0,0,HWND_MESSAGE,nullptr,module,&creation);
    if(!h && !creation.adopted)delete target;
    if(h)Log(L"Animation-only bridge ready hwnd=%p icon=%llu",h,icon);
    return h;
}
static void DescribePlayer(IXamlDiagnostics* diag,IVisualTreeService* service,InstanceHandle button,InstanceHandle icon) {
    IInspectable* element=nullptr;
    HRESULT hr=diag->GetIInspectableFromHandle(icon,&element);
    if(FAILED(hr)||!element){Log(L"Icon object unavailable hr=%08lx",hr);return;}
    IUnknown* player=nullptr;hr=element->QueryInterface(IID_Player,reinterpret_cast<void**>(&player));element->Release();
    if(FAILED(hr)||!player){Log(L"Not a WinUI2 AnimatedVisualPlayer hr=%08lx",hr);return;}
    IInspectable* source=nullptr;
    hr=Slot<HRESULT(STDMETHODCALLTYPE*)(IUnknown*,IInspectable**)>(player,8)(player,&source);
    BYTE playing=0;Slot<HRESULT(STDMETHODCALLTYPE*)(IUnknown*,BYTE*)>(player,15)(player,&playing);
    Log(L"Search icon handle=%llu button=%llu source=%s playing=%u hr=%08lx",icon,button,ClassName(source).c_str(),playing,hr);
    if(source){
        IUnknown* source2=nullptr;
        hr=source->QueryInterface(IID_Source2,reinterpret_cast<void**>(&source2));
        if(SUCCEEDED(hr)&&source2){
            IUnknown* markers=nullptr;
            hr=Slot<HRESULT(STDMETHODCALLTYPE*)(IUnknown*,IUnknown**)>(source2,6)(source2,&markers);
            if(SUCCEEDED(hr)&&markers){
                for(auto name:{L"NormalOnToNormalOff_End",L"PointerOverOffToNormalOff_End",L"NormalOffToNormalOn_End",L"NormalToPointerOver_Start"}){
                    HSTRING key=nullptr;WindowsCreateString(name,(UINT32)wcslen(name),&key);double value=-1;
                    hr=Slot<HRESULT(STDMETHODCALLTYPE*)(IUnknown*,HSTRING,double*)>(markers,6)(markers,key,&value);
                    WindowsDeleteString(key);Log(L"Marker %s = %.9f hr=%08lx",name,value,hr);
                }
                markers->Release();
            }
            source2->Release();
        }
        source->Release();
    }
    unsigned sc=0,pc=0;PropertyChainSource* sources=nullptr;PropertyChainValue* values=nullptr;
    hr=service->GetPropertyValuesChain(button,&sc,&sources,&pc,&values);
    if(SUCCEEDED(hr)){
        for(unsigned i=0;i<pc;i++) if(values[i].PropertyName && !wcscmp(values[i].PropertyName,L"IsChecked"))
            Log(L"Search IsChecked=%s overridden=%d metadata=%llu",values[i].Value,values[i].Overridden,values[i].MetadataBits);
    }
    Log(L"Button property inspection hr=%08lx",hr);
    CoTaskMemFree(sources);CoTaskMemFree(values);player->Release();
}
static std::mutex watchersMutex;
static std::vector<class Watcher*> watchers;
class Watcher : public IVisualTreeServiceCallback2 {
    std::atomic<ULONG> refs{1}; IXamlDiagnostics* diag=nullptr;IVisualTreeService* service=nullptr;
    std::map<InstanceHandle,InstanceHandle> parents;
    std::map<InstanceHandle,bool> buttons;
    std::map<InstanceHandle,HWND> bridges;
    std::mutex mutex;
    std::atomic<bool> stopping{false};
    std::mutex readyMutex; std::condition_variable readyCondition; bool ready=false;
public:
    Watcher(IUnknown* site){site->QueryInterface(IID_IXamlDiagnostics,(void**)&diag);site->QueryInterface(IID_IVisualTreeService,(void**)&service);}
    ~Watcher(){if(service)service->Release();if(diag)diag->Release();}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** out) override {
        if(!out)return E_POINTER;*out=nullptr;
        if(id==IID_IUnknown||id==IID_IAgileObject||id==IID_IVisualTreeServiceCallback||id==IID_IVisualTreeServiceCallback2){*out=static_cast<IVisualTreeServiceCallback2*>(this);AddRef();return S_OK;}return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override{ULONG n=--refs;if(!n)delete this;return n;}
    void Start(){
        if(!diag||!service){Log(L"XAML interfaces unavailable");return;}
        {std::lock_guard<std::mutex> lock(watchersMutex);AddRef();watchers.push_back(this);}
        AddRef();HANDLE t=CreateThread(nullptr,0,[](void* v)->DWORD{
            auto p=static_cast<Watcher*>(v);CoInitializeEx(nullptr,COINIT_MULTITHREADED);
            HRESULT hr=p->service->AdviseVisualTreeChange(p);Log(L"AdviseVisualTreeChange hr=%08lx",hr);
            {std::lock_guard<std::mutex> lock(p->readyMutex);p->ready=true;}p->readyCondition.notify_all();
            p->Release();CoUninitialize();return 0;
        },this,0,nullptr);
        if(t)CloseHandle(t);else{ready=true;readyCondition.notify_all();Release();}
    }
    void Stop(){
        stopping=true;
        {std::unique_lock<std::mutex> lock(readyMutex);readyCondition.wait(lock,[this]{return ready;});}
        HRESULT hr=service->UnadviseVisualTreeChange(this);
        {std::lock_guard<std::mutex> lock(mutex);for(auto& item:bridges)PostMessageW(item.second,WM_CLOSE,0,0);bridges.clear();}
        Log(L"Animation observer detached hr=%08lx",hr);
    }
    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation,VisualElement element,VisualMutationType mutation) override {
        try{
            if(stopping)return S_OK;
            InstanceHandle button=0;
            {
                std::lock_guard<std::mutex> lock(mutex);
                if(stopping)return S_OK;
                if(mutation==Remove){auto old=bridges.find(element.Handle);if(old!=bridges.end()){PostMessageW(old->second,WM_CLOSE,0,0);bridges.erase(old);}parents.erase(element.Handle);buttons.erase(element.Handle);return S_OK;}
                parents[element.Handle]=relation.Parent;
                if(element.Type && (wcsstr(element.Type,L"SearchBoxButton")||wcsstr(element.Type,L"SearchIconButton"))){buttons[element.Handle]=true;Log(L"Found Search button handle=%llu",element.Handle);}
                if(bridges.count(element.Handle))return S_OK;
                if(!element.Name||wcscmp(element.Name,L"Icon")||!element.Type||!wcsstr(element.Type,L"AnimatedVisualPlayer"))return S_OK;
                InstanceHandle p=relation.Parent;
                for(unsigned i=0;i<32&&p;i++) {if(buttons.count(p)){button=p;break;}auto it=parents.find(p);if(it==parents.end())break;p=it->second;}
            }
            if(button){DescribePlayer(diag,service,button,element.Handle);HWND h=CreateBridge(diag,service,button,element.Handle);if(h){std::lock_guard<std::mutex> lock(mutex);if(stopping)PostMessageW(h,WM_CLOSE,0,0);else bridges[element.Handle]=h;}}
        }catch(...){Log(L"Visual tree inspection failed");}
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle,VisualElementState,LPCWSTR)override{return S_OK;}
};
static void RequestDetach(){
    HANDLE t=CreateThread(nullptr,0,[](void*)->DWORD{
        CoInitializeEx(nullptr,COINIT_MULTITHREADED);
        std::vector<Watcher*> pending;{std::lock_guard<std::mutex> lock(watchersMutex);pending.swap(watchers);}
        for(auto p:pending){p->Stop();p->Release();}
        CoUninitialize();return 0;
    },nullptr,0,nullptr);if(t)CloseHandle(t);
}
class Tap : public IObjectWithSite {
    std::atomic<ULONG> refs{1};IUnknown* site=nullptr;
public:
    ~Tap(){if(site)site->Release();}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** out)override{if(!out)return E_POINTER;*out=nullptr;if(id==IID_IUnknown||id==IID_IObjectWithSite){*out=static_cast<IObjectWithSite*>(this);AddRef();return S_OK;}return E_NOINTERFACE;}
    ULONG STDMETHODCALLTYPE AddRef()override{return ++refs;}
    ULONG STDMETHODCALLTYPE Release()override{ULONG n=--refs;if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* p)override{if(site)site->Release();site=p;if(p){p->AddRef();Log(L"Animation-only diagnostic site attached");auto w=new Watcher(p);w->Start();w->Release();}return S_OK;}
    HRESULT STDMETHODCALLTYPE GetSite(REFIID id,void** out)override{return site?site->QueryInterface(id,out):E_FAIL;}
};
class Factory : public IClassFactory {
    std::atomic<ULONG> refs{1};
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** out)override{if(!out)return E_POINTER;*out=nullptr;if(id==IID_IUnknown||id==IID_IClassFactory){*out=this;AddRef();return S_OK;}return E_NOINTERFACE;}
    ULONG STDMETHODCALLTYPE AddRef()override{return ++refs;}
    ULONG STDMETHODCALLTYPE Release()override{ULONG n=--refs;if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer,REFIID id,void** out)override{if(outer)return CLASS_E_NOAGGREGATION;auto p=new Tap;HRESULT hr=p->QueryInterface(id,out);p->Release();return hr;}
    HRESULT STDMETHODCALLTYPE LockServer(BOOL)override{return S_OK;}
};
extern "C" __declspec(dllexport) HRESULT WINAPI DllGetClassObject(REFCLSID cls,REFIID id,void** out){if(cls!=CLSID_Bridge)return CLASS_E_CLASSNOTAVAILABLE;auto f=new Factory;HRESULT hr=f->QueryInterface(id,out);f->Release();return hr;}
extern "C" __declspec(dllexport) HRESULT WINAPI DllCanUnloadNow(){return S_FALSE;}

#ifdef BRIDGE_HOST
#include <tlhelp32.h>
#include <shellapi.h>
static int HostMain(int argc,wchar_t** argv){
    if(argc!=3){fwprintf(stderr,L"Usage: AnimationBridgeHost.exe <ExplorerPID> <DLLPath>\n");return 2;}
    DWORD pid=wcstoul(argv[1],nullptr,10);
    HRESULT initialized=RoInitialize(RO_INIT_MULTITHREADED);
    HMODULE m=LoadLibraryExW(L"Windows.UI.Xaml.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!m)return 3;
    auto init=reinterpret_cast<decltype(&InitializeXamlDiagnosticsEx)>(GetProcAddress(m,"InitializeXamlDiagnosticsEx"));if(!init)return 4;
    HRESULT hr=E_FAIL;
    for(int i=1;i<=1000;i++){wchar_t endpoint[64];swprintf_s(endpoint,L"VisualDiagConnection%d",i);hr=init(endpoint,pid,L"",argv[2],CLSID_Bridge,nullptr);if(hr!=HRESULT_FROM_WIN32(ERROR_NOT_FOUND)){Log(L"Host endpoint=%s HRESULT=%08lx",endpoint,hr);break;}}
    if(SUCCEEDED(initialized))RoUninitialize();return FAILED(hr)?5:0;
}
int WINAPI wWinMain(HINSTANCE,HINSTANCE,PWSTR,int){int argc=0;auto argv=CommandLineToArgvW(GetCommandLineW(),&argc);int result=HostMain(argc,argv);LocalFree(argv);return result;}
#endif
