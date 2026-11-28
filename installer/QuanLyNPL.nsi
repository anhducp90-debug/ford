; Bộ cài Quản lý Nguyên phụ liệu – cài cho người dùng hiện tại, không cần quyền quản trị.
; Build:  makensis -DVERSION=1.0.0 -DSRC=<thư mục publish> -DOUT=<file setup.exe> installer/QuanLyNPL.nsi
Unicode true
SetCompressor /SOLID lzma
RequestExecutionLevel user

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!define APPNAME "Quản lý Nguyên phụ liệu"
!define APPKEY "QuanLyNPL"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPKEY}"

Name "${APPNAME} ${VERSION}"
OutFile "${OUT}"
InstallDir "$LOCALAPPDATA\Programs\QuanLyNPL"
InstallDirRegKey HKCU "Software\${APPKEY}" "InstallDir"
BrandingText "${APPNAME} ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileDescription" "Bộ cài ${APPNAME}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "QuanLyNPL"

!include "MUI2.nsh"
!define MUI_ICON "${SRC}\app.ico"
!define MUI_UNICON "${SRC}\app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "Trình cài đặt sẽ cài ${APPNAME} ${VERSION} lên máy tính.$\r$\n$\r$\nPhần mềm chạy offline trên một máy, dữ liệu lưu tại:$\r$\n%LOCALAPPDATA%\QuanLyNPL\data.db$\r$\n$\r$\nLần đầu mở phần mềm, bạn sẽ tạo mã PIN để khóa ứng dụng."
!define MUI_FINISHPAGE_RUN "$INSTDIR\QuanLyNPL.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Mở ${APPNAME} ngay"
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\HUONG_DAN.txt"
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Xem hướng dẫn sử dụng nhanh"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Vietnamese"

Section "Cài đặt"
  ; Đóng phiên bản đang chạy (nếu có) để ghi đè tệp khi nâng cấp
  nsExec::Exec 'taskkill /IM QuanLyNPL.exe /F'
  SetOutPath "$INSTDIR"
  File /r "${SRC}\*.*"
  WriteUninstaller "$INSTDIR\GoCaiDat.exe"
  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\QuanLyNPL.exe" "" "$INSTDIR\app.ico"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\Hướng dẫn sử dụng.lnk" "$INSTDIR\HUONG_DAN.txt"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\Gỡ cài đặt.lnk" "$INSTDIR\GoCaiDat.exe"
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\QuanLyNPL.exe" "" "$INSTDIR\app.ico"
  WriteRegStr HKCU "Software\${APPKEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "QuanLyNPL"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\app.ico"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\GoCaiDat.exe"'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  nsExec::Exec 'taskkill /IM QuanLyNPL.exe /F'
  Delete "$DESKTOP\${APPNAME}.lnk"
  RMDir /r "$SMPROGRAMS\${APPNAME}"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "Software\${APPKEY}"
  ; Không xóa dữ liệu %LOCALAPPDATA%\QuanLyNPL (data.db, sao lưu) để tránh mất dữ liệu.
  MessageBox MB_OK "Đã gỡ phần mềm. Dữ liệu vẫn được giữ tại $LOCALAPPDATA\QuanLyNPL (xóa thủ công nếu không cần)."
SectionEnd
