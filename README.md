# portfolio-gameserver-csharp
C# 게임서버 포트폴리오

# 소개
C# 게임서버 포트폴리오입니다.


게임룸에 입장한 모든 플레이어들의 이동 및 전투를 동기화 하는 게임서버입니다. 


# 기능
:heavy_check_mark: 서버 설정(Config) 관리


:heavy_check_mark: 세션 관리


:heavy_check_mark: 패킷 처리


:heavy_check_mark: 데이터 관리


:heavy_check_mark: 게임룸 입장 및 관리


:heavy_check_mark: 맵


:heavy_check_mark: 플레이어 이동 동기화


:heavy_check_mark: 플레이어 스킬 처리


:heavy_check_mark: 플레이어 Hit 판정 처리


:heavy_check_mark: 플레이어 Damage 처리


:heavy_check_mark: 적->플레이어 Search AI


:heavy_check_mark: 적->플레이어 Skill AI


# 게임 설정(Config) 관리
- 설정과 관련된 정보를 Config.json로 작성한다.
- 서버가 시작되면 LoadConfig()를 호출하여 Config 파일을 로드 후, ServerConfig 객체에 매핑한다.
``` c#
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
```


# 세션 관리
### **SessionManager.cs**
- (캡처 필요)
- 접속된 클라이언트 세션 정보를 저장 및 관리한다.
- 클라이언트가 서버에 접속하연 Generate()를 호출하여 전용 ClientSession 객체를 생성하고 SessionId를 부여한다.
``` c#
class SessionManager
{
	static SessionManager _session = new SessionManager();
	public static SessionManager Instance { get { return _session; } }

	object _lock = new object();
	int _sessionId = 0;
	
	//접속된 모든 클라이언트 세션 관리
	Dictionary<int, ClientSession> _sessions = new Dictionary<int, ClientSession>();

	//클라이언트가 서버 접속 시 호출
	public ClientSession Generate()
	{
		lock (_lock)
		{
			int sessionId = ++_sessionId;

			ClientSession session = new ClientSession();
			session.SessionId = sessionId;
			_sessions.Add(sessionId, session);

			Console.WriteLine($"Connected : {sessionId}");

			return session;
		}
	}
		
	//...(중략)
}
```
### **ClientSession.cs**
- ClientSession 클래스는 SessionId와 해당 플레이어의 정보를 갖고 있다.
``` c#
public class ClientSession : PacketSession
{
	public Player MyPlayer { get; set; }
	public int SessionId { get; set; }

	//...(중략)
}
```
- 서버에 접속/접속해제 시 처리가 정의되어 있다.
- 생성 직후 OnConnected(EndPoint endPoint)를 호출하여 나의 플레이어 정보가 생성되고 게임룸에 입장한다.
- 접속 해제 시 OnDisconnected(EndPoint endPoint) 호출을 통해 게임룸에서 퇴장시키며 ClientSession 객제도 더 이상 SessionManager에 의해 관리되지 않게 된다.
``` c#
//접속 시 호출
public class ClientSession : PacketSession
{
	//...(중략)
		
	public override void OnConnected(EndPoint endPoint)
	{
		Console.WriteLine($"OnConnected : {endPoint}");

		//나의 플레이어 정보 생성
		MyPlayer = ObjectManager.Instance.Add<Player>();
		{
			MyPlayer.Info.Name = $"Player_{MyPlayer.Info.ObjectId}";
			MyPlayer.Info.PosInfo.State = CreatureState.Idle;
			MyPlayer.Info.PosInfo.MoveDir = MoveDir.Down;
			MyPlayer.Info.PosInfo.PosX = 0;
			MyPlayer.Info.PosInfo.PosY = 0;

			StatInfo stat = null;
			DataManager.StatDict.TryGetValue(1, out stat);
			MyPlayer.Stat.MergeFrom(stat);

			MyPlayer.Session = this;
		}

		//게임룸 입장
		GameRoom room = RoomManager.Instance.Find(1);
		room.Push(room.EnterGame, MyPlayer);
	}
		
	//접속 해제 시 호출
	public override void OnDisconnected(EndPoint endPoint)
	{
		//게임룸 퇴장
		GameRoom room = RoomManager.Instance.Find(1);
		room.Push(room.LeaveGame, MyPlayer.Info.ObjectId);

		//세션매니저에서 해제
		SessionManager.Instance.Remove(this);

		Console.WriteLine($"OnDisconnected : {endPoint}");
	}
	
	//...(중략)
}
```
- 클라로부터 패킷을 수신/송신 시 처리가 정의되어 있다.
- (추가 설명 필요) 패킷 변환 
``` c#
public class ClientSession : PacketSession
{
	//...(중략)
	
	public void Send(IMessage packet)
	{
		string msgName = packet.Descriptor.Name.Replace("_", string.Empty);
		MsgId msgId = (MsgId)Enum.Parse(typeof(MsgId), msgName);
		ushort size = (ushort)packet.CalculateSize();
		byte[] sendBuffer = new byte[size + 4];
		Array.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, sendBuffer, 0, sizeof(ushort)); //버퍼 사이즈 값 추가
		Array.Copy(BitConverter.GetBytes((ushort)msgId), 0, sendBuffer, 2, sizeof(ushort)); //msgId 값 추가
		Array.Copy(packet.ToByteArray(), 0, sendBuffer, 4, size);
	
		Send(new ArraySegment<byte>(sendBuffer));
	}

	public override void OnRecvPacket(ArraySegment<byte> buffer)
	{
		PacketManager.Instance.OnRecvPacket(this, buffer);
	}
	
	//...(중략)
}
```


# 패킷 처리
### **PacketManager.cs**
- (캡처 필요)
- 초기화 시 Register()를 호출하여 패킷 수신 시 처리해야 할 핸들러를 등록한다.
``` c#
class PacketManager
{
	#region Singleton
	static PacketManager _instance = new PacketManager();
	public static PacketManager Instance { get { return _instance; } }
	#endregion

	PacketManager()
	{
		Register();
	}

	Dictionary<ushort, Action<PacketSession, ArraySegment<byte>, ushort>> _onRecv = new Dictionary<ushort, Action<PacketSession, ArraySegment<byte>, ushort>>();
	Dictionary<ushort, Action<PacketSession, IMessage>> _handler = new Dictionary<ushort, Action<PacketSession, IMessage>>();
		
	public Action<PacketSession, IMessage, ushort> CustomHandler { get; set; }

	//패킷 수신 시 처리해야 할 핸들러를 등록 
	public void Register()
	{		
		_onRecv.Add((ushort)MsgId.CMove, MakePacket<C_Move>);
		_handler.Add((ushort)MsgId.CMove, PacketHandler.C_MoveHandler);		
		_onRecv.Add((ushort)MsgId.CSkill, MakePacket<C_Skill>);
		_handler.Add((ushort)MsgId.CSkill, PacketHandler.C_SkillHandler);
	}
	
	//...(중략)
}	
```
- 패킷을 수신하면 특정 패킷에 맞는 핸들러를 찾아 실행한다. 
- (추가 설명 필요) id, size 관련
``` c#
class PacketManager
{
	//...(중략)
	
	public void OnRecvPacket(PacketSession session, ArraySegment<byte> buffer)
	{
		ushort count = 0;

		ushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset);
		count += 2;
		ushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + count);
		count += 2;

		Action<PacketSession, ArraySegment<byte>, ushort> action = null;
		if (_onRecv.TryGetValue(id, out action))
			action.Invoke(session, buffer, id);
	}
	
	//...(중략)
}
```


### **PacketHandler.cs**
- 각 컨텐츠 관련 패킷에 대한 실질적인 처리가 이루어지는 클래스이다.
- C_MoveHandler는 플레이어 이동 패킷을 처리한다.
- C_SkillHandler는 플레이어 스킬 발동 패킷을 처리한다.
``` c#
class PacketHandler
{
	//플레이어 패킷 이동 패킷 처리 핸들러
	public static void C_MoveHandler(PacketSession session, IMessage packet)
	{
		C_Move movePacket = packet as C_Move;
		ClientSession clientSession = session as ClientSession;

		//Console.WriteLine($"C_Move ({movePacket.PosInfo.PosX}, {movePacket.PosInfo.PosY})");

		Player player = clientSession.MyPlayer;
		if (player == null)
			return;

		GameRoom room = player.Room;
		if (room == null)
			return;

		room.Push(room.HandleMove, player, movePacket);
	}

    	//플레이어 스킬 발동 패킷 처리 핸들러
    	public static void C_SkillHandler(PacketSession session, IMessage packet)
	{
		C_Skill skillPacket = packet as C_Skill;
		ClientSession clientSession = session as ClientSession;

		Player player = clientSession.MyPlayer;
		if (player == null)
			return;

		GameRoom room = player.Room;
		if (room == null)
			return;

		room.Push(room.HandleSkill, player, skillPacket);
	}
}
```


# 게임 데이터 관리


### **DataManager.cs**
- 게임에 필요한 데이터 Dictionary 형태로 관리한다.
- 게임 시작 시 json 파일을 읽어와 dictionary에 저장한다.
``` c#
public interface ILoader<Key, Value>
{
	Dictionary<Key, Value> MakeDict();
}

public class DataManager
{
	//데이터는 Dictionary 형태로 관리
	public static Dictionary<int, StatInfo> StatDict { get; private set; } = new Dictionary<int, StatInfo>();
	public static Dictionary<int, Data.Skill> SkillDict { get; private set; } = new Dictionary<int, Data.Skill>();

	public static void LoadData()
	{
		//스탯데이터 로드
		StatDict = LoadJson<Data.StatData, int, StatInfo>("StatData").MakeDict();
		//스킬데이터 로드
		SkillDict = LoadJson<Data.SkillData, int, Data.Skill>("SkillData").MakeDict();
	}

	static Loader LoadJson<Loader, Key, Value>(string path) where Loader : ILoader<Key, Value>
	{
		string text = File.ReadAllText($"{ConfigManager.Config.dataPath}/{path}.json");
		return Newtonsoft.Json.JsonConvert.DeserializeObject<Loader>(text);
	}
	
	//...(중략)
}
}
```


# 게임룸 입장 및 관리


### **RoomManager.cs**
- 게임에 존재하는 게임룸을 관리한다.
- 게임룸 생성, 해제, 특정 게임룸 검색 등의 기능을 수행한다.
``` c#
public class RoomManager
{
	public static RoomManager Instance { get; } = new RoomManager();

	//dictionary에 존재하는 룸 저장 및 관리
	Dictionary<int, GameRoom> _rooms = new Dictionary<int, GameRoom>();
	object _lock = new object();
	int _roomId = 1;
		
	//게임룸 추가
	public GameRoom Add(int mapId)
	{
		GameRoom gameRoom = new GameRoom();
		gameRoom.Push(gameRoom.Init, mapId);

		lock (_lock)
		{
			gameRoom.RoomId = _roomId;
			_rooms.Add(_roomId, gameRoom);
			_roomId++;
		}

		return gameRoom;
	}

	//게임룸 삭제
	public bool Remove(int roomId)
	{
		lock (_lock)
		{
			return _rooms.Remove(roomId);
		}
	}

	//게임룸 검색
	public GameRoom Find(int roomId)
	{
		lock (_lock)
		{
			GameRoom room = null;
			if (_rooms.TryGetValue(roomId, out room))
				return room;

			return null;
		}
	}
}
```


### **GameRoom.cs**
- 게임룸 객체로 게임룸Id, 맵데이터, 게임룸 내에 존재하는 다양한 객체를 관리한다.
- 초기화 시 특정 맵Id에 해당하는 맵데이터를 로드한다.
- 플레이어의 이동 동기화, 스킬 처리, 판정 처리 등 게임룸 내에서 이루어지는 행위를 실질적으로 처리하는 객체이다.
``` c#
public class GameRoom : JobSerializer
{
	//게임룸 Id
	public int RoomId { get; set; }

	//게임룸 내에 존재하는 객체들을 dictionary 형태로 관리한다.
	Dictionary<int, Player> _players = new Dictionary<int, Player>();
	Dictionary<int, Monster> _monsters = new Dictionary<int, Monster>();
	Dictionary<int, Projectile> _projectiles = new Dictionary<int, Projectile>();

	//맵 데이터
	public Map Map { get; private set; } = new Map();

	//게임룸 초기화
	public void Init(int mapId)
	{
		Map.LoadMap(mapId);
	}
		
	//...이하 생략
}
```
- 서버가 지정한 프레임레이트에 맞게 Update()가 주기적으로 호출된다.
- Update()에서는 게임룸 내 객체들의 상태를 업데이트하고, JobQueue 쌓인 작업(주로 패킷 처리)를 수행한다. 
``` c#
public class GameRoom : JobSerializer
{
	//...(중략)
	
	public void Update()
	{
		//게임 내 객체 업데이트 처리
		foreach (Monster monster in _monsters.Values)
		{
			monster.Update();
		}

		foreach (Projectile projectile in _projectiles.Values)
		{
			projectile.Update();
		}

		//JobQueue에 쌓인 Job을 순차적으로 실행한다.
		Flush();
	}
	
	//...(중략)
}
``` 
- EnterGame(GameObject gameObject) 함수로 게임룸에 추가되는 객체를 저장하고 다른 클라이언트 세션에게 그 내용을 통보한다.
``` c#
//게임 입장

public class GameRoom : JobSerializer
{
	//...(중략)
	
	public void EnterGame(GameObject gameObject)
	{
		if (gameObject == null)
			return;

		GameObjectType type = ObjectManager.GetObjectTypeById(gameObject.Id);

		//플레이어일 경우
		if (type == GameObjectType.Player)
		{
			Player player = gameObject as Player;
			_players.Add(gameObject.Id, player);
			player.Room = this;

			Map.ApplyMove(player, new Vector2Int(player.CellPos.x, player.CellPos.y));

			// 본인한테 정보 전송
			{
				S_EnterGame enterPacket = new S_EnterGame();
				enterPacket.Player = player.Info;
				player.Session.Send(enterPacket);

				S_Spawn spawnPacket = new S_Spawn();
				foreach (Player p in _players.Values)
				{
					if (player != p)
						spawnPacket.Objects.Add(p.Info);
				}

				foreach (Monster m in _monsters.Values)
					spawnPacket.Objects.Add(m.Info);

				foreach (Projectile p in _projectiles.Values)
					spawnPacket.Objects.Add(p.Info);

				player.Session.Send(spawnPacket);
			}
		}
		//몬스터 NPC일 경우
		else if (type == GameObjectType.Monster)
		{
			Monster monster = gameObject as Monster;
			_monsters.Add(gameObject.Id, monster);
			monster.Room = this;

			Map.ApplyMove(monster, new Vector2Int(monster.CellPos.x, monster.CellPos.y));
		}
		//투사체일 경우
		else if (type == GameObjectType.Projectile)
		{
			Projectile projectile = gameObject as Projectile;
			_projectiles.Add(gameObject.Id, projectile);
			projectile.Room = this;
		}
	
		// 타인한테 정보 전송
		{
			S_Spawn spawnPacket = new S_Spawn();
			spawnPacket.Objects.Add(gameObject.Info);
			foreach (Player p in _players.Values)
			{
				if (p.Id != gameObject.Id)
					p.Session.Send(spawnPacket);
			}
		}
	}
	
	//...(중략)
}
```
- LeaveGame(int objectId) 함수를 통해 게임룸에서 삭제 및 퇴장하는 객체를 해체하고 다른 클라이언트 세션에게 그 내용을 통보한다.
``` c#

//...(중략)

//게임 퇴장
public void LeaveGame(int objectId)
{
	GameObjectType type = ObjectManager.GetObjectTypeById(objectId);

	if (type == GameObjectType.Player)
	{
		Player player = null;
		if (_players.Remove(objectId, out player) == false)
			return;

		Map.ApplyLeave(player);
		player.Room = null;

		// 본인한테 정보 전송
		{
			S_LeaveGame leavePacket = new S_LeaveGame();
			player.Session.Send(leavePacket);
		}
	}
	else if (type == GameObjectType.Monster)
	{
		Monster monster = null;
		if (_monsters.Remove(objectId, out monster) == false)
			return;

		Map.ApplyLeave(monster);
		monster.Room = null;
	}
	else if (type == GameObjectType.Projectile)
	{
		Projectile projectile = null;
		if (_projectiles.Remove(objectId, out projectile) == false)
			return;

		projectile.Room = null;
	}

	// 타인한테 정보 전송
	{
		S_Despawn despawnPacket = new S_Despawn();
		despawnPacket.ObjectIds.Add(objectId);
		foreach (Player p in _players.Values)
		{
			if (p.Id != objectId)
				p.Session.Send(despawnPacket);
		}
	}
}

//...(중략)
```


# 맵 
- (캡처 필요) 그리드 형태의 맵
### **Map.cs**
- 맵은 2d 그리드 형태로 Map 객체는 맵에 대한 모든 데이터를 담고 있다.
- Map 객체는 맵의 최소/최대 좌표, 맵의 크기, 장애물들에 대한 정보를 갖고 있다.
- ApplyMove()를 호출하여 캐릭터를 현재 좌표에서 목표 좌표로 이동시킨다.
``` c#
public class Map
{
	//맵 최소/최대 좌표
	public int MinX { get; set; }
	public int MaxX { get; set; }
	public int MinY { get; set; }
	public int MaxY { get; set; }

	//맵 크기
	public int SizeX { get { return MaxX - MinX + 1; } }
	public int SizeY { get { return MaxY - MinY + 1; } }

	//장애물들에 대한 정보
	bool[,] _collision;
	GameObject[,] _objects;

	//목표 좌표로 이동 가능한지 확인
	public bool CanGo(Vector2Int cellPos, bool checkObjects = true)
	{
		if (cellPos.x < MinX || cellPos.x > MaxX)
			return false;
		if (cellPos.y < MinY || cellPos.y > MaxY)
			return false;

		int x = cellPos.x - MinX;
		int y = MaxY - cellPos.y;
		return !_collision[y, x] && (!checkObjects || _objects[y, x] == null);
	}

	//목표 좌표로 도착 처리
	public bool ApplyMove(GameObject gameObject, Vector2Int dest)
	{
		ApplyLeave(gameObject);

		if (gameObject.Room == null)
			return false;
		if (gameObject.Room.Map != this)
			return false;

		PositionInfo posInfo = gameObject.PosInfo;
		if (CanGo(dest, true) == false)
			return false;

		{
			int x = dest.x - MinX;
			int y = MaxY - dest.y;
			_objects[y, x] = gameObject;
		}

		// 실제 좌표 이동
		posInfo.PosX = dest.x;
		posInfo.PosY = dest.y;
		return true;
	}
	
	//특정 좌표에서 객체 떠남 처리
	public bool ApplyLeave(GameObject gameObject)
	{
		if (gameObject.Room == null)
			return false;
		if (gameObject.Room.Map != this)
			return false;

		PositionInfo posInfo = gameObject.PosInfo;
		if (posInfo.PosX < MinX || posInfo.PosX > MaxX)
			return false;
		if (posInfo.PosY < MinY || posInfo.PosY > MaxY)
			return false;

		{
			int x = posInfo.PosX - MinX;
			int y = MaxY - posInfo.PosY;
			if (_objects[y, x] == gameObject)
				_objects[y, x] = null;
		}

		return true;
	}
}
```

# JobQueue 디자인 패턴 
- (캡처 필요) 디자인 패턴 도식화
### **JobSerializer.cs**
- 패킷 핸들러 처리는 서버 lock을 최소화 하기 위해 Command 패턴을 사용한다.
``` c#
//패킷 핸들러 처리는 서버 lock을 최소화 하기 위해 JobQueue 방식을 사용한다.
public class JobSerializer
{
	JobTimer _timer = new JobTimer();
	Queue<IJob> _jobQueue = new Queue<IJob>();
	object _lock = new object();
	bool _flush = false;

	//...(중략)
}
```
- 핸들러를 Job으로 변환하여 _jobQueue에 넣어준다.
``` c#
public class JobSerializer
{
	//...(중략)
	
	public void Push(IJob job)
	{
		lock (_lock)
		{
			_jobQueue.Enqueue(job);
		}
	}
	
	//...(중략)
}
```
- 게임룸에서 특정 시간 주기로 Tick이 발동되며 Flush를 호출한다.
- Flush()에서 _jobQueue에 쌓여있는 것들을 차례로 실행한다.
``` c#	
public class JobSerializer
{
	//...(중략)
	
	public void Flush()
	{
		_timer.Flush();

		while (true)
		{
			IJob job = Pop();
			if (job == null)
				return;

			job.Execute();
		}
	}
	
	//...(중략)
}
```


# 플레이어 이동 동기화


# 플레이어 스킬 처리


# 플레이어 Hit 판정 처리


# 플레이어 Damage 처리


# 적->플레이어 Search AI


# 적->플레이어 Skill AI
