@echo off
echo Setting up Gradle Wrapper...

if not exist gradlew.bat (
    echo Downloading Gradle Wrapper...
    powershell -Command "Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/gradle/gradle/v8.9.0/gradle/wrapper/gradle-wrapper.jar' -OutFile 'gradle/wrapper/gradle-wrapper.jar'"
    powershell -Command "Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/gradle/gradle/v8.9.0/gradle/wrapper/gradlew.bat' -OutFile 'gradlew.bat'"
    powershell -Command "Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/gradle/gradle/v8.9.0/gradle/wrapper/gradlew' -OutFile 'gradlew'"
    echo Gradle Wrapper downloaded.
)

echo Building Release APK...
gradlew.bat assembleRelease --no-daemon

if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    exit /b 1
)

echo.
echo Build complete!
echo APK location: app\build\outputs\apk\release\app-release-unsigned.apk
echo.
pause
