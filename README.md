# protoshop-backend

To create zip for deployment
dotnet publish ProtoshopBackend.csproj -c Release --runtime win-x64 --self-contained false
cd bin/Release/net8.0/win-x64/publish
zip -r ../../../../deployment.zip ./*