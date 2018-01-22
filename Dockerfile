FROM microsoft/dotnet:1-sdk
COPY . /find-idle-instances
WORKDIR /find-idle-instances
RUN dotnet restore
RUN dotnet build
ENTRYPOINT ["dotnet", "run"]

