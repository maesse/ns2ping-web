
# NS2 Server Browser

A web-based server browser for Natural Selection 2 written in C#

This project is running at [NS2Browser.com](https://ns2browser.com)

Backend is a JSON API running on ASP.NET with a background service handling server pinging using [Valve server query protocol](https://developer.valvesoftware.com/wiki/Server_queries).

Frontend is [Alpine.JS](https://alpinejs.dev/) + [Bootstrap](https://getbootstrap.com/)


## Features

- Live display of servers using WebSockets
- Plays a sound when a server becomes joinable
- Open the game and connect directly with a single click
- Automatically grabs a fresh list of available servers from Valves master server
- Automatically stops background service when the frontend is inactive
- Docker ready

## Screenshots

![App Screenshot](https://user-images.githubusercontent.com/82190/250655155-dac196db-1316-437b-bafc-ccb1e317a639.PNG)


## Usage/Examples

Normal `dotnet` commands will run the project:
```
dotnet build
dotnet run

```

## Authors

- [Mads Lind / @maesse](https://www.github.com/maesse)