# portfolio-gameserver-csharp
C# 게임서버 포트폴리오

## 소개
C# 게임서버 포트폴리오입니다.

## 기능
:heavy_check_mark: ConfigManager


:heavy_check_mark: SessionManager


:heavy_check_mark: PacketManager


:heavy_check_mark: DataManager


:heavy_check_mark: RoomManager


:heavy_check_mark: JobManager


## ConfigManager
``` c#
namespace Server.Data
{
	//설정과 관련된 정보를 저장하는 클래스
	[Serializable]
	public class ServerConfig
	{
		public string dataPath;
		public string connectionIP;
		public string connectionPort;
	}

	public class ConfigManager
	{
		public static ServerConfig Config { get; private set; }

		//config.json파일을 로드하여 Config 객체에 매핑해 준다.
		public static void LoadConfig()
		{
			string text = File.ReadAllText("config.json");
			Config = Newtonsoft.Json.JsonConvert.DeserializeObject<ServerConfig>(text);
		}
	}
}
```
## SessionManager
## PacketManager
## DataManager
## RoomManager
## JobManager
