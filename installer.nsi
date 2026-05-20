!include "MUI2.nsh"

Name "Scarl Professional"
OutFile "Scarl_Installer.exe"
InstallDir "$PROGRAMFILES64\Scarl"
RequestExecutionLevel admin

!define MUI_ABORTWARNING
!define MUI_ICON "Scarl.UI\Assets\logo.ico"

; Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "MainSection" SEC01
    SetOutPath "$INSTDIR"
    File "Scarl.UI\bin\Release\net11.0-windows\Scarl.UI.exe"
    File "Scarl.UI\bin\Release\net11.0-windows\Scarl.UI.dll"
    File "Scarl.UI\bin\Release\net11.0-windows\Scarl.UI.runtimeconfig.json"
    File "Scarl.UI\bin\Release\net11.0-windows\scarl_core.dll"
    File "Scarl.UI\bin\Release\net11.0-windows\DirectML.dll"
    
    SetOutPath "$INSTDIR\models"
    File /r "models\*.*"
    
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    
    ; Shortcuts
    CreateShortcut "$DESKTOP\Scarl.lnk" "$INSTDIR\Scarl.UI.exe"
SectionEnd

Section "Uninstall"
    Delete "$DESKTOP\Scarl.lnk"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir /r "$INSTDIR"
SectionEnd
