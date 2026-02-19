; ============================================================================
; PSForge NSIS Installer Script
; ============================================================================
; Produces a standard Windows installer that:
;   - Detects and offers to install .NET 8.0 Desktop Runtime if missing
;   - Installs to Program Files (per-machine) or AppData (per-user)
;   - Creates Start Menu shortcuts and uninstaller
;   - Supports silent installation (/S flag)
;
; Build:  makensis installer.nsi
; Requires: NSIS 3.x (https://nsis.sourceforge.io)
; ============================================================================

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

; ── Version info (updated by update-application.ps1) ────────────────────────
!define PRODUCT_NAME      "PSForge"
!define PRODUCT_VERSION   "1.0.0"
!define PRODUCT_PUBLISHER "Swatto"
!define PRODUCT_WEB_SITE  ""
!define PRODUCT_EXE       "PSForge.exe"
!define DOTNET_VERSION    "8.0"
!define DOTNET_RUNTIME_URL "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"

; ── Output & source dirs ────────────────────────────────────────────────────
!define PUBLISH_DIR       "bin\publish"
!define INSTALLER_OUTPUT  "bin\installer"
OutFile "${INSTALLER_OUTPUT}\PSForge-${PRODUCT_VERSION}-Setup.exe"

; ── Installer attributes ────────────────────────────────────────────────────
Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
Unicode true
ManifestDPIAware true
RequestExecutionLevel admin
InstallDir "$PROGRAMFILES64\${PRODUCT_NAME}"
InstallDirRegKey HKLM "Software\${PRODUCT_NAME}" "InstallDir"

; ── Compression ─────────────────────────────────────────────────────────────
SetCompressor /SOLID lzma
SetCompressorDictSize 32

; ── MUI settings ────────────────────────────────────────────────────────────
!define MUI_ABORTWARNING
!define MUI_ICON "${PUBLISH_DIR}\${PRODUCT_EXE}"
!define MUI_UNICON "${PUBLISH_DIR}\${PRODUCT_EXE}"

; ── Pages ───────────────────────────────────────────────────────────────────
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ============================================================================
; Functions
; ============================================================================

Function .onInit
  ; Ensure 64-bit Windows
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "PSForge requires 64-bit Windows."
    Abort
  ${EndIf}
FunctionEnd

; ── .NET Runtime Detection ──────────────────────────────────────────────────
; Checks for .NET Desktop Runtime 8.0+ via 'dotnet --list-runtimes'
Function CheckDotNetRuntime
  nsExec::ExecToStack 'dotnet --list-runtimes'
  Pop $0  ; exit code
  Pop $1  ; stdout

  ; Look for Microsoft.WindowsDesktop.App 8.x in the output
  ${If} $0 == 0
    StrCpy $2 0
    ; Simple string search for the runtime line
    ${WordFind} $1 "Microsoft.WindowsDesktop.App ${DOTNET_VERSION}" "E+1{" $3
    IfErrors 0 +2
      StrCpy $2 0
      Goto DotNetNotFound
    StrCpy $2 1
  ${Else}
    StrCpy $2 0
  ${EndIf}

  ${If} $2 == 1
    ; Runtime found — continue
    Goto DotNetDone
  ${EndIf}

  DotNetNotFound:
    MessageBox MB_YESNO|MB_ICONQUESTION \
      ".NET ${DOTNET_VERSION} Desktop Runtime is required but was not detected.$\n$\n\
       Would you like to download and install it now?" \
      IDYES DownloadDotNet IDNO AbortInstall

  DownloadDotNet:
    DetailPrint "Downloading .NET ${DOTNET_VERSION} Desktop Runtime..."
    inetc::get /CAPTION "Downloading .NET Runtime" /BANNER "Please wait..." \
      "${DOTNET_RUNTIME_URL}" "$TEMP\dotnet-desktop-runtime.exe" /END
    Pop $0
    ${If} $0 != "OK"
      MessageBox MB_OK|MB_ICONSTOP "Failed to download .NET Runtime: $0"
      Abort
    ${EndIf}

    DetailPrint "Installing .NET ${DOTNET_VERSION} Desktop Runtime..."
    ExecWait '"$TEMP\dotnet-desktop-runtime.exe" /install /quiet /norestart' $0
    ${If} $0 != 0
      MessageBox MB_OK|MB_ICONSTOP "The .NET Runtime installer returned error code $0.$\nPlease install .NET ${DOTNET_VERSION} Desktop Runtime manually."
      Abort
    ${EndIf}
    Delete "$TEMP\dotnet-desktop-runtime.exe"
    DetailPrint ".NET Runtime installed successfully."
    Goto DotNetDone

  AbortInstall:
    MessageBox MB_OK|MB_ICONSTOP \
      "PSForge cannot run without .NET ${DOTNET_VERSION} Desktop Runtime.$\nInstallation will now abort."
    Abort

  DotNetDone:
FunctionEnd

; ============================================================================
; Install Section
; ============================================================================
Section "Install" SecInstall
  ; Check for .NET runtime before installing files
  Call CheckDotNetRuntime

  SetOutPath "$INSTDIR"

  ; Copy all published files (preserves subdirectory structure)
  File /r "${PUBLISH_DIR}\*.*"

  ; Write uninstaller
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; ── Registry entries ──────────────────────────────────────────────────────
  WriteRegStr HKLM "Software\${PRODUCT_NAME}" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "Software\${PRODUCT_NAME}" "Version" "${PRODUCT_VERSION}"

  ; Add/Remove Programs entry
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" \
    "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" \
    "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" \
    "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" \
    "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" \
    "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" \
    "DisplayIcon" '"$INSTDIR\${PRODUCT_EXE}"'
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" \
    "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" \
    "NoRepair" 1

  ; Calculate installed size
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" \
    "EstimatedSize" $0

  ; ── Start Menu shortcuts ──────────────────────────────────────────────────
  CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
  CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk" \
    "$INSTDIR\${PRODUCT_EXE}" "" "$INSTDIR\${PRODUCT_EXE}" 0
  CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall ${PRODUCT_NAME}.lnk" \
    "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0

SectionEnd

; ============================================================================
; Uninstall Section
; ============================================================================
Section "Uninstall"
  ; Remove Start Menu shortcuts
  Delete "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk"
  Delete "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall ${PRODUCT_NAME}.lnk"
  RMDir "$SMPROGRAMS\${PRODUCT_NAME}"

  ; Remove installed files — remove the entire install directory
  RMDir /r "$INSTDIR"

  ; Remove registry entries
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
  DeleteRegKey HKLM "Software\${PRODUCT_NAME}"

SectionEnd
