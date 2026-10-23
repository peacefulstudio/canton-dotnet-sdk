@echo off
rem Copyright 2026 Peaceful Studio OÜ
rem SPDX-License-Identifier: Apache-2.0
setlocal enableextensions disabledelayedexpansion

set "BUNDLE_ROOT=%~dp0"
set "HELPER_JAR=%BUNDLE_ROOT%bin\daml-dar-to-proto.jar"
set "EMITTER=%BUNDLE_ROOT%bin\Daml.Codegen.CSharp.Cli.exe"

set "DAR="
set "OUT="
set "EMITTER_ARGS="
set "AFTER_DOUBLE_DASH="
set "HAS_GENERATE_PROJECT="
set "HAS_RUNTIME_VERSION="
set "PUBLISH_NUGET="
set "NUGET_CONFIG="
set "NUGET_SOURCE="

:parse
if "%~1"=="" goto after_parse
if defined AFTER_DOUBLE_DASH goto emitter_arg
for %%o in (--dar --out --nuget-config --nuget-source) do if /I "%~1"=="%%o" if "%~2"=="" (echo dpm-codegen-cs: %%o requires a value 1>&2 & goto usage)
if /I "%~1"=="--dar"           (set "DAR=%~2" & shift & shift & goto parse)
if /I "%~1"=="--out"           (set "OUT=%~2" & shift & shift & goto parse)
if /I "%~1"=="--publish-nuget" (set "PUBLISH_NUGET=1" & shift & goto parse)
if /I "%~1"=="--nuget-config"  (set "NUGET_CONFIG=%~2" & shift & shift & goto parse)
if /I "%~1"=="--nuget-source"  (set "NUGET_SOURCE=%~2" & shift & shift & goto parse)
if /I "%~1"=="-h"              goto help
if /I "%~1"=="--help"          goto help
if /I "%~1"=="--"              (set "AFTER_DOUBLE_DASH=1" & shift & goto parse)

:emitter_arg
set EMITTER_ARGS=%EMITTER_ARGS% "%~1"
if /I "%~1"=="--generate-project" set "HAS_GENERATE_PROJECT=1"
if /I "%~1"=="--runtime-version" set "HAS_RUNTIME_VERSION=1"
set "ARG_HEAD=%~1"
if /I "%ARG_HEAD:~0,18%"=="--runtime-version=" set "HAS_RUNTIME_VERSION=1"
shift
goto parse

:after_parse
if not defined DAR goto usage
if not defined OUT goto usage
if defined PUBLISH_NUGET if not defined NUGET_CONFIG echo dpm-codegen-cs: --nuget-config is required when --publish-nuget is set 1>&2 & goto usage
if defined PUBLISH_NUGET if not defined NUGET_SOURCE echo dpm-codegen-cs: --nuget-source is required when --publish-nuget is set 1>&2 & goto usage
if defined PUBLISH_NUGET where dotnet >nul 2>nul
if defined PUBLISH_NUGET if errorlevel 1 echo dpm-codegen-cs: 'dotnet' not found on PATH ^(.NET SDK required for --publish-nuget^) 1>&2 & exit /b 1

if not exist "%DAR%" echo dpm-codegen-cs: DAR not found: %DAR% 1>&2 & exit /b 1
if not exist "%HELPER_JAR%" echo dpm-codegen-cs: bundled JVM helper missing: %HELPER_JAR% 1>&2 & exit /b 1
if not exist "%EMITTER%" echo dpm-codegen-cs: bundled emitter missing: %EMITTER% 1>&2 & exit /b 1
where java >nul 2>nul
if errorlevel 1 echo dpm-codegen-cs: 'java' not found on PATH ^(dpm install precondition: JDK 17+^) 1>&2 & exit /b 1

if defined PUBLISH_NUGET if not defined HAS_GENERATE_PROJECT set EMITTER_ARGS=%EMITTER_ARGS% --generate-project
if defined PUBLISH_NUGET if not defined HAS_RUNTIME_VERSION echo dpm-codegen-cs: warning: --runtime-version not set; generated .csproj will reference Daml.Runtime with wildcard version ^(*^) 1>&2

if not exist "%OUT%" mkdir "%OUT%"
set "INTERMEDIATE=%TEMP%\dpm-codegen-cs-%RANDOM%-%RANDOM%.binpb"

java -jar "%HELPER_JAR%" --dar "%DAR%" --out "%INTERMEDIATE%"
set "HELPER_RC=%ERRORLEVEL%"
if %HELPER_RC% neq 0 del /q "%INTERMEDIATE%" 2>nul & exit /b %HELPER_RC%

"%EMITTER%" --intermediate "%INTERMEDIATE%" -o "%OUT%" %EMITTER_ARGS%
set "EMITTER_RC=%ERRORLEVEL%"
del /q "%INTERMEDIATE%" 2>nul
if %EMITTER_RC% neq 0 exit /b %EMITTER_RC%
if not defined PUBLISH_NUGET exit /b 0

dotnet pack "%OUT%" -c Release
set "PACK_RC=%ERRORLEVEL%"
if %PACK_RC% neq 0 exit /b %PACK_RC%

set "NUPKG="
set "NUPKG_COUNT=0"
for %%f in ("%OUT%\bin\Release\*.nupkg") do call :record_nupkg "%%f"
if not defined NUPKG echo dpm-codegen-cs: no .nupkg produced 1>&2 & exit /b 1
if %NUPKG_COUNT% gtr 1 echo dpm-codegen-cs: multiple .nupkg files found under "%OUT%\bin\Release"; refusing to guess which to push 1>&2 & exit /b 1

dotnet nuget push "%NUPKG%" --configfile "%NUGET_CONFIG%" --source "%NUGET_SOURCE%" --skip-duplicate
exit /b %ERRORLEVEL%

:record_nupkg
set "NUPKG_NAME=%~n1"
if /I "%NUPKG_NAME:~-8%"==".symbols" exit /b 0
set "NUPKG=%~1"
set /a NUPKG_COUNT+=1
exit /b 0

:help
call :print_usage
exit /b 0

:usage
call :print_usage 1>&2
exit /b 2

:print_usage
echo Usage: dpm-codegen-cs --dar ^<path-to-dar^> --out ^<output-dir^> [--publish-nuget --nuget-config ^<path^> --nuget-source ^<name^>] [--] [emitter-args...]
exit /b 0
