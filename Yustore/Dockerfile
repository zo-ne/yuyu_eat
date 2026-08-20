# 建置 context 是 repo 根目錄（讓 Directory.Build.props / .editorconfig 能被抓到）：
#   docker build -f Yustore/Dockerfile -t yustore .
# 平常建議直接用根目錄的 docker-compose.yml，不用自己記這串指令。

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 先只複製 .csproj 再 restore，這樣程式碼改動不會讓 NuGet 套件重新下載（layer cache）
COPY Directory.Build.props ./
COPY Yustore/Yustore.csproj Yustore/
RUN dotnet restore Yustore/Yustore.csproj

COPY Yustore/ Yustore/
WORKDIR /src/Yustore
RUN dotnet build Yustore.csproj -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish Yustore.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 圖片上傳存在容器裡的這個路徑，docker-compose.yml 會把它掛成 volume，
# 容器重建後上傳的檔案才不會不見
RUN mkdir -p /app/wwwroot/uploads

ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Yustore.dll"]
