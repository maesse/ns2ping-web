
# NS2 Server Browser

A web-based server browser for Natural Selection 2 written in C#

Backend is a JSON API running on ASP.NET with a background service handling server pinging.

Frontend is [Alpine.JS](https://alpinejs.dev/) + [Bootstrap](https://getbootstrap.com/)


## Features

- Live display of servers
- Automatically stops background service when the frontend is inactive
- Plays a sound when a server becomes joinable
- Open the game and connect directly with a single click
- Docker ready

## Screenshots

![App Screenshot](https://user-images.githubusercontent.com/82190/243192950-98dbc3aa-8dff-4d24-8526-5b9405da5892.PNG)


## Usage/Examples

Normal `dotnet` commands will run the project:
```
dotnet build
dotnet run

```

This project uses a pre-defined list of servers. A config file will automatically be written to `config/config.json` if the file doesn't exist. This file can be modified to add your own favorite servers.

If using Docker you can mount a docker volume to /App/config to enable persistance of the config file. Sample docker commands are available in `commands.txt`
## Todo

- Add/Remove/Modify servers using the web interface
- Allow for personalized view of servers using cookies (for multiuser support)
## Authors

- [Mads Lind / @maesse](https://www.github.com/maesse)

