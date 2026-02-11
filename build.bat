@echo off
setlocal

set "MSBUILD=C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
set "PROJECT=%~dp0SDProfileManager\SDProfileManager.csproj"
set "PUBLISH_EXE=%~dp0SDProfileManager\bin\x64\Release\net9.0-windows10.0.22621.0\win-x64\publish\SDProfileManager.exe"
set "ROOT_EXE=%~dp0SDProfileManager.exe"

REM Publish a single-file, self-contained executable for easy launching from repo root.
"%MSBUILD%" "%PROJECT%" -t:Publish -restore -p:Configuration=Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:EnableMsixTooling=true -p:RestoreIgnoreFailedSources=true -p:NuGetAudit=false %*
if errorlevel 1 exit /b %errorlevel%

copy /Y "%PUBLISH_EXE%" "%ROOT_EXE%" >nul
if errorlevel 1 (
  echo Failed to copy published executable to repo root.
  exit /b 1
)

echo Wrote %ROOT_EXE%
