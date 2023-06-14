# portfolio-gameserver-csharp
2D action game server implemented using C#

# Introduction
This is 2D action game server implemented using C#


It is a game server that synchronizes the movement and battle of all players who have entered the game room.


# Implementations
:heavy_check_mark: Server configuration management


:heavy_check_mark: Session management


:heavy_check_mark: Packet processing


:heavy_check_mark: Game data management


:heavy_check_mark: Game room management


:heavy_check_mark: Map


:heavy_check_mark: JobQueue


:heavy_check_mark: Player movement 


:heavy_check_mark: Player combat and hit detection


:heavy_check_mark: NPC AI - Search player


:heavy_check_mark: NPC AI - Attack player


# Server configuration management
- Write configuration-related information as Config.json.
- When the server starts, LoadConfig() is called to load the config file and map it to the ServerConfig object.
``` c#
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

	//Load config.json file and map it to Config object.
	public static void LoadConfig()
	{
		string text = File.ReadAllText("config.json");
		Config = Newtonsoft.Json.JsonConvert.DeserializeObject<ServerConfig>(text);
	}
}
```


# Session management
### **SessionManager.cs**
- Stores and manages connected client session information.
- When a client connects to the server, Generate() is called to create a dedicated ClientSession object and give it a SessionId.
``` c#
class SessionManager
{
	static SessionManager _session = new SessionManager();
	public static SessionManager Instance { get { return _session; } }

	object _lock = new object();
	int _sessionId = 0;
	
	//Stores all connected client sessions
	Dictionary<int, ClientSession> _sessions = new Dictionary<int, ClientSession>();

	//Called when client connects to server
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
		
	//...(omitted)
}
```
### **ClientSession.cs**
- The ClientSession class holds the SessionId and information about the player.
``` c#
public class ClientSession : PacketSession
{
	public Player MyPlayer { get; set; }
	public int SessionId { get; set; }

	//...(omitted)
}
```
- Processing is defined when connecting/disconnecting from the server.
- Right after creation, I call OnConnected(EndPoint endPoint) to create my player and enter the game room.
- When disconnected, OnDisconnected(EndPoint endPoint) is called to exit the game room, and the ClientSession object is no longer managed by the SessionManager.


![mmo_unity_1](https://github.com/hyklux/portfolio-gameserver-csharp/assets/96270683/8d8bbd43-6b22-45a7-8773-2ea817472c4f)


``` c#
public class ClientSession : PacketSession
{
	//...(omitted)
	
	//Called when connected
	public override void OnConnected(EndPoint endPoint)
	{
		Console.WriteLine($"OnConnected : {endPoint}");

		//Create my player info
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

		//Enter game room
		GameRoom room = RoomManager.Instance.Find(1);
		room.Push(room.EnterGame, MyPlayer);
	}
		
	//Called when disconnected
	public override void OnDisconnected(EndPoint endPoint)
	{
		//Exit game room
		GameRoom room = RoomManager.Instance.Find(1);
		room.Push(room.LeaveGame, MyPlayer.Info.ObjectId);

		//Removes from session manager
		SessionManager.Instance.Remove(this);

		Console.WriteLine($"OnDisconnected : {endPoint}");
	}
	
	//...(omitted)
}
```
- Processing is defined when receiving/transmitting packets from the release client in the session manager.
``` c#
public class ClientSession : PacketSession
{
	//...(omitted)
	
	public void Send(IMessage packet)
	{
		string msgName = packet.Descriptor.Name.Replace("_", string.Empty);
		MsgId msgId = (MsgId)Enum.Parse(typeof(MsgId), msgName);
		ushort size = (ushort)packet.CalculateSize();
		byte[] sendBuffer = new byte[size + 4];
		Array.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, sendBuffer, 0, sizeof(ushort)); //Add buffer size value
		Array.Copy(BitConverter.GetBytes((ushort)msgId), 0, sendBuffer, 2, sizeof(ushort)); //Add msgId value
		Array.Copy(packet.ToByteArray(), 0, sendBuffer, 4, size);
	
		Send(new ArraySegment<byte>(sendBuffer));
	}

	public override void OnRecvPacket(ArraySegment<byte> buffer)
	{
		PacketManager.Instance.OnRecvPacket(this, buffer);
	}
	
	//...(omitted)
}
```


# Packet processing
### **PacketManager.cs**
- Register() is called upon initialization to register handlers to be processed when packets are received.
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

	//Registers handlers to be processed when packets are received
	public void Register()
	{		
		_onRecv.Add((ushort)MsgId.CMove, MakePacket<C_Move>);
		_handler.Add((ushort)MsgId.CMove, PacketHandler.C_MoveHandler);		
		_onRecv.Add((ushort)MsgId.CSkill, MakePacket<C_Skill>);
		_handler.Add((ushort)MsgId.CSkill, PacketHandler.C_SkillHandler);
	}
	
	//...(omitted)
}	
```
- When a packet is received, it finds and executes a handler for that particular packet.
``` c#
class PacketManager
{
	//...(omitted)
	
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
	
	//...(omitted)
}
```


### **PacketHandler.cs**
- This is the class where the actual processing of each content-related packet is done.
- C_MoveHandler handles player movement packets.
- C_SkillHandler handles player skill activation packets.
``` c#
class PacketHandler
{
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


# Game data management


### **DataManager.cs**
- Data necessary for the game is managed in a dictionary.
- When the game starts, the json file is read and stored in the dictionary.
``` c#
public interface ILoader<Key, Value>
{
	Dictionary<Key, Value> MakeDict();
}

public class DataManager
{
	public static Dictionary<int, StatInfo> StatDict { get; private set; } = new Dictionary<int, StatInfo>();
	public static Dictionary<int, Data.Skill> SkillDict { get; private set; } = new Dictionary<int, Data.Skill>();

	public static void LoadData()
	{
		//Loads stat data
		StatDict = LoadJson<Data.StatData, int, StatInfo>("StatData").MakeDict();
		//Loads skill data
		SkillDict = LoadJson<Data.SkillData, int, Data.Skill>("SkillData").MakeDict();
	}

	static Loader LoadJson<Loader, Key, Value>(string path) where Loader : ILoader<Key, Value>
	{
		string text = File.ReadAllText($"{ConfigManager.Config.dataPath}/{path}.json");
		return Newtonsoft.Json.JsonConvert.DeserializeObject<Loader>(text);
	}
	
	//...(omitted)
}
```


# Game room management


### **RoomManager.cs**
- Manages the game rooms that exist in the game.
- It performs functions such as creating and releasing game rooms, and searching for specific game rooms.
``` c#
public class RoomManager
{
	public static RoomManager Instance { get; } = new RoomManager();

	//Stores current existing game rooms
	Dictionary<int, GameRoom> _rooms = new Dictionary<int, GameRoom>();
	object _lock = new object();
	int _roomId = 1;
		
	//Adds a game room
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

	//Removes a game room
	public bool Remove(int roomId)
	{
		lock (_lock)
		{
			return _rooms.Remove(roomId);
		}
	}

	//Finds a game room
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
- As a game room object, it manages game room ID, map data, and various objects that exist in the game room.
- When initializing, map data corresponding to a specific map ID is loaded.
- This is an object that actually handles actions within the game room, such as player movement synchronization, skill processing, and hit detection.
``` c#
public class GameRoom : JobSerializer
{
	public int RoomId { get; set; }

	Dictionary<int, Player> _players = new Dictionary<int, Player>();
	Dictionary<int, Monster> _monsters = new Dictionary<int, Monster>();
	Dictionary<int, Projectile> _projectiles = new Dictionary<int, Projectile>();

	public Map Map { get; private set; } = new Map();

	public void Init(int mapId)
	{
		Map.LoadMap(mapId);
	}
		
	//...(omitted)
}
```
- Update() is called periodically according to the frame rate specified by the server.
- Update() updates the state of objects in the game room and performs tasks (mainly packet processing) stacked in the JobQueue.
``` c#
public class GameRoom : JobSerializer
{
	//...(omitted)
	
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

		//The jobs accumulated in the JobQueue are sequentially executed.
		Flush();
	}
	
	//...(omitted)
}
``` 
- EnterGame(GameObject gameObject) function saves objects that are added to the game room and notifies other client sessions.
``` c#
public class GameRoom : JobSerializer
{
	//...(omitted)
	
	public void EnterGame(GameObject gameObject)
	{
		if (gameObject == null)
			return;

		GameObjectType type = ObjectManager.GetObjectTypeById(gameObject.Id);

		if (type == GameObjectType.Player)
		{
			Player player = gameObject as Player;
			_players.Add(gameObject.Id, player);
			player.Room = this;

			Map.ApplyMove(player, new Vector2Int(player.CellPos.x, player.CellPos.y));

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
		else if (type == GameObjectType.Monster)
		{
			Monster monster = gameObject as Monster;
			_monsters.Add(gameObject.Id, monster);
			monster.Room = this;

			Map.ApplyMove(monster, new Vector2Int(monster.CellPos.x, monster.CellPos.y));
		}
		else if (type == GameObjectType.Projectile)
		{
			Projectile projectile = gameObject as Projectile;
			_projectiles.Add(gameObject.Id, projectile);
			projectile.Room = this;
		}
	
		S_Spawn spawnPacket = new S_Spawn();
		spawnPacket.Objects.Add(gameObject.Info);
		foreach (Player p in _players.Values)
		{
			if (p.Id != gameObject.Id)
				p.Session.Send(spawnPacket);
		}
	}
	
	//...(omitted)
}
```
- LeaveGame(int objectId) function destroys objects that are being deleted and left from the game room and notifies other client sessions.
``` c#
public class GameRoom : JobSerializer
{
	//...(omitted)

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

			S_LeaveGame leavePacket = new S_LeaveGame();
			player.Session.Send(leavePacket);
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

	//...(omitted)
}
```


# Map 
### **Map.cs**
- A map is in the form of a 2d grid, and the Map object contains all the data about the map.
- The Map object holds information about the map's minimum/maximum coordinates, map size, and obstacles.
- Call ApplyMove() to move the character from the current coordinates to the target coordinates.
``` c#
public class Map
{
	public int MinX { get; set; }
	public int MaxX { get; set; }
	public int MinY { get; set; }
	public int MaxY { get; set; }

	public int SizeX { get { return MaxX - MinX + 1; } }
	public int SizeY { get { return MaxY - MinY + 1; } }

	bool[,] _collision;
	GameObject[,] _objects;

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

		posInfo.PosX = dest.x;
		posInfo.PosY = dest.y;
		return true;
	}
	
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

# JobQueue
### **JobSerializer.cs**
- Packet handler processing uses the Command pattern to minimize server locks.
``` c#
//Packet handler processing uses JobQueue method to minimize server lock.
public class JobSerializer
{
	JobTimer _timer = new JobTimer();
	Queue<IJob> _jobQueue = new Queue<IJob>();
	object _lock = new object();
	bool _flush = false;

	//...(omitted)
}
```
- Convert the handler to Job and put it in _jobQueue.
``` c#
public class JobSerializer
{
	//...(omitted)
	
	public void Push(IJob job)
	{
		lock (_lock)
		{
			_jobQueue.Enqueue(job);
		}
	}
	
	//...(omitted)
}
```
- In the game room, Tick is triggered at a specific time period and Flush is called.
``` c#
class Program
{
	static Listener _listener = new Listener();
	static List<System.Timers.Timer> _timers = new List<System.Timers.Timer>();

	static void TickRoom(GameRoom room, int tick = 100)
	{
		var timer = new System.Timers.Timer();
		timer.Interval = tick;
		timer.Elapsed += ((s, e) => { room.Update(); });
		timer.AutoReset = true;
		timer.Enabled = true;

		_timers.Add(timer);
	}
	
	//...(omitted)
}
```
- Flush() executes the items piled up in _jobQueue in turn.
``` c#	
public class JobSerializer
{
	//...(omitted)
	
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
	
	//...(omitted)
}
```


# Player movement
### **GameRoom.cs**
- When we receive a C_Move packet from the client session, we process the move for that player.
- After checking if it is possible to move to the target coordinates, move it, and notify the result of the movement to other client sessions to synchronize them.
``` c#
public class GameRoom : JobSerializer
{
	//...(omitted)
	
	public void HandleMove(Player player, C_Move movePacket)
	{
		if (player == null)
			return;

		PositionInfo movePosInfo = movePacket.PosInfo;
		ObjectInfo info = player.Info;

		// Check if the player can move to the destination position
		if (movePosInfo.PosX != info.PosInfo.PosX || movePosInfo.PosY != info.PosInfo.PosY)
		{
			if (Map.CanGo(new Vector2Int(movePosInfo.PosX, movePosInfo.PosY)) == false)
				return;
		}

		info.PosInfo.State = movePosInfo.State;
		info.PosInfo.MoveDir = movePosInfo.MoveDir;
		Map.ApplyMove(player, new Vector2Int(movePosInfo.PosX, movePosInfo.PosY));

		// Broadcast to other players so that they can syncronize my player
		S_Move resMovePacket = new S_Move();
		resMovePacket.ObjectId = player.Info.ObjectId;
		resMovePacket.PosInfo = movePacket.PosInfo;
		Broadcast(resMovePacket);
	}
	
	//...(omitted)
}
```


# Player combat and hit detection
### **GameRoom.cs**
- When the C_Skill packet is received from the client session, it performs skill processing for that player.
- Notifies other client sessions that the skill has been triggered.
- It provides processing defined according to the skill type.
- In the case of a melee attack, the hit judgment process is processed immediately, but in the case of a projectile attack, only the projectile is created and the actual hit processing is handled in the projectile logic.
``` c#
public class GameRoom : JobSerializer
{
	//...(omitted)
	

	public void HandleSkill(Player player, C_Skill skillPacket)
	{
		if (player == null)
			return;

		ObjectInfo info = player.Info;
		if (info.PosInfo.State != CreatureState.Idle)
			return;

		info.PosInfo.State = CreatureState.Skill;
		
		//Broadcast the activated skill to other players
		S_Skill skill = new S_Skill() { Info = new SkillInfo() };
		skill.ObjectId = info.ObjectId;
		skill.Info.SkillId = skillPacket.Info.SkillId;
		Broadcast(skill);

		Data.Skill skillData = null;
		if (DataManager.SkillDict.TryGetValue(skillPacket.Info.SkillId, out skillData) == false)
			return;

		switch (skillData.skillType)
		{
			case SkillType.SkillAuto: //normal melee attack
				{
					Vector2Int skillPos = player.GetFrontCellPos(info.PosInfo.MoveDir);
					GameObject target = Map.Find(skillPos);
					//Melee attack damages are dealt with immediately
					if (target != null)
					{
						Console.WriteLine("Hit GameObject !");
						target.OnDamaged(player, player.Stat.Attack);
					}
				}
				break;
			case SkillType.SkillProjectile: //원거리 화살 공격
				{
					//Arrow attacks create only arrow projectiles, and deal with collisions and hits in the arrow logic.
					Arrow arrow = ObjectManager.Instance.Add<Arrow>();
					if (arrow == null)
						return;

					arrow.Owner = player;
					arrow.Data = skillData;
					arrow.PosInfo.State = CreatureState.Moving;
					arrow.PosInfo.MoveDir = player.PosInfo.MoveDir;
					arrow.PosInfo.PosX = player.PosInfo.PosX;
					arrow.PosInfo.PosY = player.PosInfo.PosY;
					arrow.Speed = skillData.projectile.speed;
					Push(EnterGame, arrow);
				}
				break;
		}
	}
	
	//...(omitted)
}
```


### **GameObject.cs**
- GameObject is the superclass of all objects in the game.
- Player is also a subclass of GameObject, and damage reduction and death are handled here.
- Damage processing is performed in the OnDamaged function, and death processing is continued in the OnDead function if the HP is below 0.
- In damage handling and death handling, each client session is notified of the corresponding contents.
``` c#
public class GameObject
{
	//...(omitted)
	
	public virtual void OnDamaged(GameObject attacker, int damage)
	{
		if (Room == null)
			return;

		Stat.Hp = Math.Max(Stat.Hp - damage, 0);

		//Broadcast to other client sessions
		S_ChangeHp changePacket = new S_ChangeHp();
		changePacket.ObjectId = Id;
		changePacket.Hp = Stat.Hp;
		Room.Broadcast(changePacket);

		if (Stat.Hp <= 0)
		{
			OnDead(attacker);
		}
	}

	public virtual void OnDead(GameObject attacker)
	{
		if (Room == null)
			return;

		//Broadcast the death of the player to other client sessions
		S_Die diePacket = new S_Die();
		diePacket.ObjectId = Id;
		diePacket.AttackerId = attacker.Id;
		Room.Broadcast(diePacket);

		//When player is dead, the player is removed from the game room.
		GameRoom room = Room;
		room.LeaveGame(Id);
	}
	
	//...(omitted)
}
```


# NPC AI - Search player
### **Monster.cs**
- Monster NPC AI is implemented as FSM (Finite State Machine).
- Update() is called every frame and performs actions according to the current state.
``` c#
public class Monster : GameObject
{
	public Monster()
	{
		ObjectType = GameObjectType.Monster;
	
		Stat.Level = 1;
		Stat.Hp = 100;
		Stat.MaxHp = 100;
		Stat.Speed = 5.0f;
	
		State = CreatureState.Idle;
	}
	
	// FSM (Finite State Machine)
	public override void Update()
	{
		switch (State)
		{
			case CreatureState.Idle:
				UpdateIdle();
				break;
			case CreatureState.Moving:
				UpdateMoving();
				break;
			case CreatureState.Skill:
				UpdateSkill();
				break;
			case CreatureState.Dead:
				UpdateDead();
				break;
		}
	}
	
	//...(omitted)
}
```
- In the Idle state, it searches for players within a given range.
- When a player is found, it is designated as a target to be tracked and the monster's status is updated from Idle to Moving.
``` c#
public class Monster : GameObject
{
	//...(omitted)
		
	Player _target;
	int _searchCellDist = 10;
	int _chaseCellDist = 20;
	
	protected virtual void UpdateIdle()
	{
		if (_nextSearchTick > Environment.TickCount64)
			return;
			
		_nextSearchTick = Environment.TickCount64 + 1000;

		//Set to target if there is a player in the search range (_searchCellDist)
		Player target = Room.FindPlayer(p =>
		{
			Vector2Int dir = p.CellPos - CellPos;
			return dir.cellDistFromZero <= _searchCellDist;
		});

		if (target == null)
			return;

		_target = target;
		
		//If there is a target, the status changes to Moving
		State = CreatureState.Moving;
	}
	
	//...(omitted)
}
```
- After searching for the optimal path with the FindPath() function of Map.cs, move along the path.
- Be aware of exceptions, such as players moving too far or leaving the room, and handle exceptions carefully.
``` c#
public class Monster : GameObject
{
	//...(omitted)
	
	int _skillRange = 1;
	long _nextMoveTick = 0;
	protected virtual void UpdateMoving()
	{
		if (_nextMoveTick > Environment.TickCount64)
			return;
		int moveTick = (int)(1000 / Speed);
		_nextMoveTick = Environment.TickCount64 + moveTick;

		if (_target == null || _target.Room != Room)
		{
			_target = null;
			State = CreatureState.Idle;
			BroadcastMove();
			return;
		}

		Vector2Int dir = _target.CellPos - CellPos;
		int dist = dir.cellDistFromZero;
		if (dist == 0 || dist > _chaseCellDist)
		{
			_target = null;
			State = CreatureState.Idle;
			BroadcastMove();
			return;
		}

		//Finding the shortest path (based on the A* algorithm)
		List<Vector2Int> path = Room.Map.FindPath(CellPos, _target.CellPos, checkObjects: false);
		if (path.Count < 2 || path.Count > _chaseCellDist)
		{
			_target = null;
			State = CreatureState.Idle;
			BroadcastMove();
			return;
		}

		if (dist <= _skillRange && (dir.x == 0 || dir.y == 0))
		{
			_coolTick = 0;
			State = CreatureState.Skill;
			return;
		}

		Dir = GetDirFromVec(path[1] - CellPos);
		Room.Map.ApplyMove(this, path[1]);
		BroadcastMove();
	}
	
	//...(omitted)
}
```


# NPC AI - Attack player
### **Monster.cs**
- When the monster NPC tracks the player and catches up enough to use the skill, it updates its status to the skill.
- In the UpdateSkill() function, we trigger the skill on the player.
``` c#
public class Monster : GameObject
{
	//...(omitted)

	long _coolTick = 0;
	protected virtual void UpdateSkill()
	{
		if (_coolTick == 0)
		{
			if (_target == null || _target.Room != Room || _target.Hp == 0)
			{
				_target = null;
				State = CreatureState.Moving;
				BroadcastMove();
				return;
			}

			Vector2Int dir = (_target.CellPos - CellPos);
			int dist = dir.cellDistFromZero;
			bool canUseSkill = (dist <= _skillRange && (dir.x == 0 || dir.y == 0));
			if (canUseSkill == false)
			{
				State = CreatureState.Moving;
				BroadcastMove();
				return;
			}

			// Set to target direction
			MoveDir lookDir = GetDirFromVec(dir);
			if (Dir != lookDir)
			{
				Dir = lookDir;
				BroadcastMove();
			}

			Skill skillData = null;
			DataManager.SkillDict.TryGetValue(1, out skillData);

			// Take damage
			_target.OnDamaged(this, skillData.damage + Stat.Attack);

			// Broadcast to other players
			S_Skill skill = new S_Skill() { Info = new SkillInfo() };
			skill.ObjectId = Id;
			skill.Info.SkillId = skillData.id;
			Room.Broadcast(skill);

			// Apply skill cooltime
			int coolTick = (int)(1000 * skillData.cooldown);
			_coolTick = Environment.TickCount64 + coolTick;
		}

		if (_coolTick > Environment.TickCount64)
			return;

		_coolTick = 0;
	}
	
	//...(omitted)
}
```
