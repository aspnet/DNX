#include "stdafx.h"
#include "HostAssemblyStore.h"
#include "FileStream.h"
#include "ComObject.h"
#include <string>

const wchar_t* AppDomainManagerTypeName = L"dnx.clr.managed, Version=1.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60, ProcessorArchitecture=MSIL";

namespace
{
    bool FileExists(const std::wstring& path)
    {
        auto attributes = GetFileAttributes(path.c_str());

        return attributes != INVALID_FILE_ATTRIBUTES && ((attributes & FILE_ATTRIBUTE_DIRECTORY) == 0);
    }
}

// IHostAssemblyStore
HRESULT STDMETHODCALLTYPE HostAssemblyStore::ProvideAssembly(AssemblyBindInfo *pBindInfo, UINT64 *pAssemblyId, UINT64 *pContext,
    IStream **ppStmAssemblyImage, IStream **ppStmPDB)
{
    if (_wcsicmp(AppDomainManagerTypeName, pBindInfo->lpReferencedIdentity) == 0)
    {
        wchar_t default_lib[MAX_PATH] = { 0 };
        if (GetEnvironmentVariable(L"DNX_DEFAULT_LIB", default_lib, MAX_PATH) == 0)
        {
            return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
        }

        std::wstring path(default_lib);
        if (path.back() != L'\\')
        {
            path.append(L"\\");
        }
        path.append(L"dnx.clr.managed.dll");

        if (path.length() > MAX_PATH || !FileExists(path))
        {
            return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
        }

        auto assembly_stream = new ComObject<FileStream>();
        if (FAILED(assembly_stream->QueryInterface(IID_PPV_ARGS(ppStmAssemblyImage))) ||
            FAILED((static_cast<FileStream*>(*ppStmAssemblyImage))->Open(path.c_str())))
        {
            *ppStmAssemblyImage = NULL;
            delete assembly_stream;

            return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
        }

        path.replace(path.length() - 3, 3, L"pdb");
        if (FileExists(path.c_str()))
        {
            auto pdb_stream = new ComObject<FileStream>();
            if (FAILED(pdb_stream->QueryInterface(IID_PPV_ARGS(ppStmPDB))) ||
                FAILED((static_cast<FileStream*>(*ppStmPDB))->Open(path.c_str())))
            {
                *ppStmPDB = NULL;
                delete pdb_stream;
            }
        }

        return S_OK;
    }

    return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
}

HRESULT STDMETHODCALLTYPE HostAssemblyStore::ProvideModule(ModuleBindInfo *pBindInfo, DWORD *pdwModuleId, IStream **ppStmModuleImage,
    IStream **ppStmPDB)
{
    return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
}

// IUnknown
HRESULT STDMETHODCALLTYPE HostAssemblyStore::QueryInterface(const IID &iid, void **ppv)
{
    if (!ppv)
    {
        return E_POINTER;
    }

    *ppv = this;
    AddRef();
    return S_OK;
}

ULONG STDMETHODCALLTYPE HostAssemblyStore::AddRef()
{
    return InterlockedIncrement(&m_RefCount);
}

ULONG STDMETHODCALLTYPE HostAssemblyStore::Release()
{
    if (InterlockedDecrement(&m_RefCount) == 0)
    {
        delete this;
        return 0;
    }
    return m_RefCount;
}