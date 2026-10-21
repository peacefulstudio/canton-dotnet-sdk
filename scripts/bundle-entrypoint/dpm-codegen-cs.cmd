@echo off
rem Copyright 2026 Peaceful Studio OÜ
rem SPDX-License-Identifier: Apache-2.0
setlocal enableextensions enabledelayedexpansion

set "BUNDLE_ROOT=%~dp0"
set "HELPER_JAR=%BUNDLE_ROOT%bin\daml-dar-to-proto.jar"
set "EMITTER=%BUNDLE_ROOT%bin\Daml.Codegen.CSharp.Cli.exe"

set "DAR="
set "OUT="
set "EMITTER_ARGS="
set "PUBLISH_NUGET="
set "NUGET_CONFIG="
set "NUGET_SOURCE="

:parse
if "%~1"=="" goto after_parse
if /I "%~1"=="--dar"           (set "DAR=%~2" & shift & shift & goto parse)
if /I "%~1"=="--out"           (set "OUT=%~2" & shift & shift & goto parse)
if /I "%~1"=="--publish-nuget" (set "PUBLISH_NUGET=1" & shift & goto parse)
if /I "%~1"=="--nuget-config"  (set "NUGET_CONFIG=%~2" & shift & shift & goto parse)
if /I "%~1"=="--nuget-source"  (set "NUGET_SOURCE=%~2" & shift & shift & goto parse)
if /I "%~1"=="-h"              goto usage
if /I "%~1"=="--help"          goto usage
if /I "%~1"=="--"              (shift & goto collect_rest)
set EMITTER_ARGS=!EMITTER_ARGS! "%~1"
shift
goto parse

:collect_rest
if "%~1"=="" goto after_parse
set EMITTER_ARGS=!EMITTER_ARGS! "%~1"
shift
goto collect_rest

:after_parse
if defined PUBLISH_NUGET if not defined NUGET_CONFIG (
  echo dpm-codegen-cs: --nuget-config is required when --publish-nuget is set 1>&2
  goto usage
)
if defined PUBLISH_NUGET if not defined NUGET_SOURCE (
  echo dpm-codegen-cs: --nuget-source is required when --publish-nuget is set 1>&2
  goto usage
)
if defined PUBLISH_NUGET (
  where dotnet >nul 2>nul
  if errorlevel 1 (
    echo dpm-codegen-cs: 'dotnet' not found on PATH ^(.NET SDK required for --publish-nuget^) 1>&2
    exit /b 1
  )
)

if not defined DAR goto usage
if not defined OUT goto usage

if not exist "%DAR%" (
  echo dpm-codegen-cs: DAR not found: %DAR% 1>&2
  exit /b 1
)
if not exist "%HELPER_JAR%" (
  echo dpm-codegen-cs: bundled JVM helper missing: %HELPER_JAR% 1>&2
  exit /b 1
)
if not exist "%EMITTER%" (
  echo dpm-codegen-cs: bundled emitter missing: %EMITTER% 1>&2
  exit /b 1
)
where java >nul 2>nul
if errorlevel 1 (
  echo dpm-codegen-cs: 'java' not found on PATH ^(dpm install precondition: JDK 17+^) 1>&2
  exit /b 1
)

if defined PUBLISH_NUGET (
  set "_HAS_GEN_PROJECT="
  for %%a in (!EMITTER_ARGS!) do (
    if /I "%%~a"=="--generate-project" set "_HAS_GEN_PROJECT=1"
  )
  if not defined _HAS_GEN_PROJECT set EMITTER_ARGS=!EMITTER_ARGS! --generate-project
  set "_HAS_RUNTIME_VERSION="
  for %%a in (!EMITTER_ARGS!) do (
    set "_tok=%%~a"
    if /I "!_tok!"=="--runtime-version" set "_HAS_RUNTIME_VERSION=1"
    if /I "!_tok:~0,18!"=="--runtime-version=" set "_HAS_RUNTIME_VERSION=1"
  )
  if not defined _HAS_RUNTIME_VERSION (
    echo dpm-codegen-cs: warning: --runtime-version not set; generated .csproj will reference Daml.Runtime with wildcard version ^(*^) 1>&2
  )
)

if not exist "%OUT%" mkdir "%OUT%"
set "INTERMEDIATE=%TEMP%\dpm-codegen-cs-%RANDOM%-%RANDOM%.binpb"

java -jar "%HELPER_JAR%" --dar "%DAR%" --out "%INTERMEDIATE%"
if errorlevel 1 (
  del /q "%INTERMEDIATE%" 2>nul
  exit /b 1
)

"%EMITTER%" --intermediate "%INTERMEDIATE%" -o "%OUT%" !EMITTER_ARGS!
set "EMITTER_RC=%ERRORLEVEL%"
del /q "%INTERMEDIATE%" 2>nul
if %EMITTER_RC% neq 0 exit /b %EMITTER_RC%

if defined PUBLISH_NUGET (
  dotnet pack "%OUT%" -c Release
  if errorlevel 1 exit /b !ERRORLEVEL!

  set "NUPKG="
  set "NUPKG_COUNT=0"
  for %%f in ("%OUT%\bin\Release\*.nupkg") do (
    set "_name=%%~nf"
    if /I "!_name:~-8!" neq ".symbols" (
      set "NUPKG=%%f"
      set /a NUPKG_COUNT+=1
    )
  )
  if not defined NUPKG (
    echo dpm-codegen-cs: no .nupkg produced 1>&2
    exit /b 1
  )
  if !NUPKG_COUNT! gtr 1 (
    echo dpm-codegen-cs: multiple .nupkg files produced under "%OUT%\bin\Release" ^(!NUPKG_COUNT!^); refusing to guess which to push 1>&2
    exit /b 1
  )

  dotnet nuget push "!NUPKG!" --configfile "%NUGET_CONFIG%" ^
    --source "%NUGET_SOURCE%" --skip-duplicate
  exit /b !ERRORLEVEL!
)

exit /b %EMITTER_RC%

:usage
echo Usage: dpm-codegen-cs --dar ^<path-to-dar^> --out ^<output-dir^> [--publish-nuget --nuget-config ^<path^> --nuget-source ^<name^>] [--] [emitter-args...] 1>&2
exit /b 2
