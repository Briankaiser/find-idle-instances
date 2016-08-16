FROM microsoft/dotnet:latest
COPY . /find-idle-instances
WORKDIR /find-idle-instances
RUN dotnet restore
RUN dotnet build
ENTRYPOINT ["dotnet", "run"]

