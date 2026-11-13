; installer.nsi — Ohmega for Windows: installs for the current user (no admin rights), Start menu shortcut,
; listed in Settings → Apps with an uninstaller. Built by ./build.sh --installer (makensis on the shitbook).
Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"

!ifndef VERSION
  !define VERSION "1.3.1"
!endif
!ifndef FFMPEG
  !define FFMPEG "?"
!endif
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Ohmega"

Name "Ohmega"
OutFile "Ohmega-${VERSION}-Windows-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\Ohmega"
InstallDirRegKey HKCU "Software\Ohmega" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
ManifestDPIAware true
BrandingText "Ohmega ${VERSION} · ohmega.is-my.app"

!define MUI_ICON "Ohmega.ico"
!define MUI_UNICON "Ohmega.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\Ohmega.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Start Ohmega"
!define MUI_FINISHPAGE_TITLE "Ohmega is installed"
!define MUI_FINISHPAGE_TEXT "Find it in the Start menu. Drop your music in — every track, equally loud."
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "Ohmega"
VIAddVersionKey "FileDescription" "Ohmega ${VERSION} installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "CompanyName" "Dany161024"
VIAddVersionKey "LegalCopyright" "GPL-3.0 · ohmega.is-my.app"
VIAddVersionKey "Comments" "Includes FFmpeg ${FFMPEG} (GPL-3.0), Windows build by gyan.dev"

Section "Ohmega"
  SetOutPath "$INSTDIR"
  File "Ohmega.exe"
  File "Ohmega.exe.config"
  File "/oname=ffmpeg.exe" "..\vendor\ffmpeg.exe"
  File "/oname=LICENSE.txt" "LICENSE"
  File "/oname=FFmpeg-LICENSE.txt" "..\vendor\ffmpeg-LICENSE"
  File "/oname=FFmpeg-README.txt" "..\vendor\ffmpeg-README.txt"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortcut "$SMPROGRAMS\Ohmega.lnk" "$INSTDIR\Ohmega.exe" "" "$INSTDIR\Ohmega.exe" 0 SW_SHOWNORMAL "" "Every track, equally loud"

  WriteRegStr HKCU "Software\Ohmega" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "Ohmega"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "Dany161024"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\Ohmega.exe"
  WriteRegStr HKCU "${UNINSTKEY}" "URLInfoAbout" "https://ohmega.is-my.app"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" $0
SectionEnd

Section "Uninstall"
  Delete "$SMPROGRAMS\Ohmega.lnk"
  Delete "$INSTDIR\Ohmega.exe"
  Delete "$INSTDIR\Ohmega.exe.config"
  Delete "$INSTDIR\ffmpeg.exe"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$INSTDIR\FFmpeg-LICENSE.txt"
  Delete "$INSTDIR\FFmpeg-README.txt"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "Software\Ohmega"
  ; saved profiles (%APPDATA%\Ohmega) and "Ohmega Backup" folders are the user's — they stay
SectionEnd
