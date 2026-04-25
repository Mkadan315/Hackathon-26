using System.Collections.Generic;
using Godot;

namespace CampusDashGodot;

public partial class CampusDashGame : Node3D
{
	private enum PickupKind
	{
		EnergyDrink,
		Snack,
		Homework,
		Project
	}

	private enum ObstacleKind
	{
		Desk,
		TeachingAssistant,
		Professor
	}

	private sealed class RunObject
	{
		public Node3D Body = default!;
		public int Lane;
		public float Z;
		public bool IsObstacle;
		public PickupKind PickupKind;
		public ObstacleKind ObstacleKind;
		public float Radius;
	}

	private const string SavePath = "user://campus_dash_records.cfg";
	private const float LaneWidth = 3.2f;
	private const float LaneChangeSpeed = 10.0f;
	private const float StartScrollSpeed = 9.0f;
	private const float MaxScrollSpeed = 20.0f;
	private const float SpeedIncreasePerSecond = 0.18f;
	private const float SpawnZ = 42.0f;
	private const float DestroyZ = -13.0f;

	private readonly List<RunObject> _runObjects = new();
	private readonly List<MeshInstance3D> _floorTiles = new();
	private readonly string[] _laneNames = { "Right", "Middle", "Left" };
	private readonly RandomNumberGenerator _random = new();

	private Node3D _player = default!;
	private Camera3D _camera = default!;
	private Label _titleLabel = default!;
	private Label _scoreLabel = default!;
	private Label _timeLabel = default!;
	private Label _bestLabel = default!;
	private Label _statusLabel = default!;
	private Label _gameOverLabel = default!;
	private MeshInstance3D _shieldVisual = default!;

	private int _currentLane = 1;
	private float _scrollSpeed;
	private float _score;
	private float _runTime;
	private float _bestScore;
	private float _bestTime;
	private float _spawnTimer;
	private float _spawnInterval = 0.85f;
	private float _buffTimer;
	private float _slowTimer;
	private float _immuneTimer;
	private bool _gameOver;
	private int _laneMoveQueued;

	public override void _Ready()
	{
		_random.Randomize();
		LoadRecords();
		CreateScene();
		RestartGame();
	}

	public override void _Input(InputEvent inputEvent)
	{
		if (inputEvent.IsActionPressed("move_left"))
		{
			_laneMoveQueued = 1;
		}

		if (inputEvent.IsActionPressed("move_right"))
		{
			_laneMoveQueued = -1;
		}

		if (inputEvent is InputEventMouseButton mouseButton && mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
		{
			_laneMoveQueued = 1;
		}

		if (inputEvent is InputEventScreenTouch screenTouch && screenTouch.Pressed)
		{
			_laneMoveQueued = 1;
		}

		if (inputEvent is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode == Key.Left)
			{
				_laneMoveQueued = 1;
			}
			else if (key.Keycode == Key.Right)
			{
				_laneMoveQueued = -1;
			}
			else if (key.Keycode == Key.R && _gameOver)
			{
				RestartGame();
			}
			else if (key.Keycode == Key.Escape)
			{
				GetTree().Quit();
			}
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		if (_gameOver)
		{
			if (_laneMoveQueued != 0)
			{
				RestartGame();
			}

			_laneMoveQueued = 0;
			UpdateHud();
			return;
		}

		if (_laneMoveQueued != 0)
		{
			_currentLane += _laneMoveQueued;
			if (_currentLane < 0)
			{
				_currentLane = 0;
			}
			else if (_currentLane > 2)
			{
				_currentLane = 2;
			}
		}

		_laneMoveQueued = 0;

		UpdatePlayer(dt);
		UpdateRunStats(dt);
		UpdateFloor(dt);
		UpdateSpawning(dt);
		MoveRunObjects(dt);
		CheckCollisions();
		UpdateCamera(dt);
		UpdateHud();
	}

	private void CreateScene()
	{
		GetNodeOrNull<Node>("FallbackHud")?.QueueFree();

		WorldEnvironment environment = new();
		Environment env = new()
		{
			BackgroundMode = Environment.BGMode.Color,
			BackgroundColor = new Color(0.52f, 0.72f, 0.92f),
			AmbientLightSource = Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.72f, 0.78f, 0.84f)
		};
		environment.Environment = env;
		AddChild(environment);

		DirectionalLight3D sun = new()
		{
			Name = "Campus Sun",
			LightEnergy = 2.0f,
			RotationDegrees = new Vector3(-55f, -30f, 0f)
		};
		AddChild(sun);

		_player = new Node3D { Name = "Student Player" };
		AddChild(_player);

		MeshInstance3D body = CreateMesh("Student Body", new CapsuleMesh { Radius = 0.38f, Height = 1.8f }, new Color(0.1f, 0.38f, 0.86f));
		body.Position = new Vector3(0f, 0.9f, 0f);
		_player.AddChild(body);

		MeshInstance3D backpack = CreateMesh("Backpack", new BoxMesh { Size = new Vector3(0.6f, 0.7f, 0.2f) }, new Color(0.04f, 0.09f, 0.16f));
		backpack.Position = new Vector3(0f, 0.9f, -0.48f);
		_player.AddChild(backpack);

		_shieldVisual = CreateMesh("Shield Bubble", new SphereMesh { Radius = 1.1f, Height = 2.2f }, new Color(0.15f, 0.85f, 1f, 0.28f));
		_shieldVisual.Position = new Vector3(0f, 0.9f, 0f);
		_player.AddChild(_shieldVisual);
		_shieldVisual.Visible = false;

		_camera = new Camera3D
		{
			Name = "Third Person Camera",
			Position = new Vector3(0f, 4.2f, -9.5f),
			Fov = 62f,
			Current = true
		};
		AddChild(_camera);
		_camera.LookAt(new Vector3(0f, 1.0f, 10f), Vector3.Up);

		CreateFloorTiles();
		CreateLaneMarkers();
		CreateHud();
	}

	private void CreateFloorTiles()
	{
		for (int i = 0; i < 5; i++)
		{
			MeshInstance3D tile = CreateMesh(
				"Scrolling Campus Path",
				new BoxMesh { Size = new Vector3(11.5f, 0.12f, 18f) },
				i % 2 == 0 ? new Color(0.34f, 0.46f, 0.38f) : new Color(0.29f, 0.41f, 0.35f));

			tile.Position = new Vector3(0f, -0.08f, i * 18f);
			AddChild(tile);
			_floorTiles.Add(tile);
		}
	}

	private void CreateLaneMarkers()
	{
		for (int lane = 0; lane < 3; lane++)
		{
			MeshInstance3D marker = CreateMesh(
				_laneNames[lane] + " Lane Marker",
				new BoxMesh { Size = new Vector3(0.07f, 0.05f, 90f) },
				new Color(0.94f, 0.91f, 0.78f));

			marker.Position = new Vector3(LaneToX(lane), 0.02f, 34f);
			AddChild(marker);
		}
	}

	private void CreateHud()
	{
		CanvasLayer canvas = new() { Name = "HUD" };
		AddChild(canvas);

		Panel panel = new()
		{
			Position = new Vector2(18f, 18f),
			Size = new Vector2(360f, 170f)
		};
		canvas.AddChild(panel);

		_titleLabel = CreateLabel("Campus Dash", 26, new Vector2(34f, 26f), new Vector2(330f, 32f), true);
		_scoreLabel = CreateLabel("", 20, new Vector2(34f, 66f), new Vector2(330f, 28f), true);
		_timeLabel = CreateLabel("", 20, new Vector2(34f, 94f), new Vector2(330f, 28f), true);
		_bestLabel = CreateLabel("", 16, new Vector2(34f, 124f), new Vector2(330f, 48f), false);
		_statusLabel = CreateLabel("", 20, new Vector2(1000f, 26f), new Vector2(260f, 34f), true);
		_gameOverLabel = CreateLabel("", 26, new Vector2(420f, 250f), new Vector2(480f, 210f), true);

		canvas.AddChild(_titleLabel);
		canvas.AddChild(_scoreLabel);
		canvas.AddChild(_timeLabel);
		canvas.AddChild(_bestLabel);
		canvas.AddChild(_statusLabel);
		canvas.AddChild(_gameOverLabel);
	}

	private static Label CreateLabel(string text, int fontSize, Vector2 position, Vector2 size, bool bold)
	{
		Label label = new()
		{
			Text = text,
			Position = position,
			Size = size,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top
		};

		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", Colors.White);
		if (bold)
		{
			label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.5f));
			label.AddThemeConstantOverride("shadow_offset_x", 2);
			label.AddThemeConstantOverride("shadow_offset_y", 2);
		}

		return label;
	}

	private void RestartGame()
	{
		foreach (RunObject runObject in _runObjects)
		{
			runObject.Body.QueueFree();
		}

		_runObjects.Clear();
		_currentLane = 1;
		_scrollSpeed = StartScrollSpeed;
		_score = 0f;
		_runTime = 0f;
		_spawnTimer = 0f;
		_buffTimer = 0f;
		_slowTimer = 0f;
		_immuneTimer = 0f;
		_gameOver = false;
		_player.Position = new Vector3(0f, 0f, 0f);
		_shieldVisual.Visible = false;
		_gameOverLabel.Text = "";
		UpdateCamera(1f);
		UpdateHud();
	}

	private void UpdatePlayer(float dt)
	{
		Vector3 target = new(LaneToX(_currentLane), 0f, 0f);
		_player.Position = _player.Position.Lerp(target, dt * LaneChangeSpeed);
		_player.RotationDegrees = new Vector3(0f, Mathf.Sin((float)Time.GetTicksMsec() * 0.01f) * 4f, 0f);
	}

	private void UpdateRunStats(float dt)
	{
		_runTime += dt;
		_buffTimer = Mathf.Max(0f, _buffTimer - dt);
		_slowTimer = Mathf.Max(0f, _slowTimer - dt);
		_immuneTimer = Mathf.Max(0f, _immuneTimer - dt);
		_shieldVisual.Visible = _immuneTimer > 0f;

		float targetSpeed = Mathf.Min(MaxScrollSpeed, StartScrollSpeed + _runTime * SpeedIncreasePerSecond);
		_scrollSpeed = Mathf.Lerp(_scrollSpeed, targetSpeed, dt * 0.6f);

		float scoreRate = 10f;
		if (_buffTimer > 0f)
		{
			scoreRate *= 1.6f;
		}

		if (_slowTimer > 0f)
		{
			scoreRate *= 0.55f;
		}

		_score += dt * scoreRate;
	}

	private void UpdateFloor(float dt)
	{
		foreach (MeshInstance3D tile in _floorTiles)
		{
			tile.Position += new Vector3(0f, 0f, -1f) * _scrollSpeed * dt;

			if (tile.Position.Z < -20f)
			{
				tile.Position += new Vector3(0f, 0f, 90f);
			}
		}
	}

	private void UpdateSpawning(float dt)
	{
		_spawnTimer -= dt;
		if (_spawnTimer > 0f)
		{
			return;
		}

		_spawnTimer = Mathf.Max(0.42f, _spawnInterval - _runTime * 0.008f);

		int lane = _random.RandiRange(0, 2);
		float roll = _random.Randf();

		if (roll < 0.68f)
		{
			SpawnObstacle(lane);
		}
		else
		{
			SpawnPickup(lane);
		}

		if (_random.Randf() < 0.22f)
		{
			int secondLane = (lane + _random.RandiRange(1, 2)) % 3;
			SpawnObstacle(secondLane);
		}
	}

	private void SpawnObstacle(int lane)
	{
		ObstacleKind kind = (ObstacleKind)_random.RandiRange(0, 2);
		Mesh mesh;
		Color color;
		Vector3 position = new(LaneToX(lane), 0.7f, SpawnZ);

		if (kind == ObstacleKind.Desk)
		{
			mesh = new BoxMesh { Size = new Vector3(1.6f, 0.9f, 1f) };
			color = new Color(0.56f, 0.32f, 0.16f);
		}
		else if (kind == ObstacleKind.TeachingAssistant)
		{
			mesh = new CapsuleMesh { Radius = 0.42f, Height = 1.7f };
			color = new Color(0.75f, 0.24f, 0.22f);
		}
		else
		{
			mesh = new CapsuleMesh { Radius = 0.48f, Height = 1.95f };
			color = new Color(0.45f, 0.24f, 0.68f);
		}

		MeshInstance3D body = CreateMesh(kind.ToString(), mesh, color);
		body.Position = position;
		AddChild(body);

		_runObjects.Add(new RunObject
		{
			Body = body,
			Lane = lane,
			Z = SpawnZ,
			IsObstacle = true,
			ObstacleKind = kind,
			Radius = 1.05f
		});
	}

	private void SpawnPickup(int lane)
	{
		PickupKind kind = (PickupKind)_random.RandiRange(0, 3);
		Mesh mesh;
		Color color;

		if (kind == PickupKind.EnergyDrink)
		{
			mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 0.9f };
			color = new Color(0.08f, 0.8f, 0.95f);
		}
		else if (kind == PickupKind.Snack)
		{
			mesh = new SphereMesh { Radius = 0.42f, Height = 0.84f };
			color = new Color(0.96f, 0.75f, 0.19f);
		}
		else
		{
			mesh = new BoxMesh { Size = new Vector3(0.95f, 0.06f, 0.7f) };
			color = kind == PickupKind.Homework ? Colors.White : new Color(0.9f, 0.88f, 0.78f);
		}

		MeshInstance3D body = CreateMesh(kind.ToString(), mesh, color);
		body.Position = new Vector3(LaneToX(lane), 1.1f, SpawnZ);
		AddPickupSprite(body, kind);
		if (kind == PickupKind.Homework || kind == PickupKind.Project)
		{
			body.RotationDegrees = new Vector3(12f, 25f, 0f);
		}

		AddChild(body);

		_runObjects.Add(new RunObject
		{
			Body = body,
			Lane = lane,
			Z = SpawnZ,
			IsObstacle = false,
			PickupKind = kind,
			Radius = 0.85f
		});
	}

	private void MoveRunObjects(float dt)
	{
		for (int i = _runObjects.Count - 1; i >= 0; i--)
		{
			RunObject runObject = _runObjects[i];
			runObject.Z -= _scrollSpeed * dt;
			runObject.Body.Position = new Vector3(LaneToX(runObject.Lane), runObject.Body.Position.Y, runObject.Z);
			runObject.Body.RotateY(Mathf.DegToRad(runObject.IsObstacle ? 25f * dt : 115f * dt));

			if (runObject.Z < DestroyZ)
			{
				runObject.Body.QueueFree();
				_runObjects.RemoveAt(i);
			}
		}
	}

	private void CheckCollisions()
	{
		for (int i = _runObjects.Count - 1; i >= 0; i--)
		{
			RunObject runObject = _runObjects[i];
			if (runObject.Lane != _currentLane || Mathf.Abs(runObject.Z) > runObject.Radius)
			{
				continue;
			}

			if (runObject.IsObstacle)
			{
				if (_immuneTimer > 0f)
				{
					_score += 30f;
					runObject.Body.QueueFree();
					_runObjects.RemoveAt(i);
					continue;
				}

				EndGame();
				return;
			}

			ApplyPickup(runObject.PickupKind);
			runObject.Body.QueueFree();
			_runObjects.RemoveAt(i);
		}
	}

	private void ApplyPickup(PickupKind pickupKind)
	{
		if (pickupKind == PickupKind.EnergyDrink)
		{
			_buffTimer = 5f;
			_immuneTimer = 4f;
			_score += 25f;
		}
		else if (pickupKind == PickupKind.Snack)
		{
			_immuneTimer = Mathf.Max(_immuneTimer, 2f);
			_score += 45f;
		}
		else if (pickupKind == PickupKind.Homework)
		{
			_slowTimer = 4f;
			_score = Mathf.Max(0f, _score - 18f);
		}
		else
		{
			_slowTimer = 6f;
			_score = Mathf.Max(0f, _score - 35f);
		}
	}

	private void EndGame()
	{
		_gameOver = true;

		if (_score > _bestScore)
		{
			_bestScore = _score;
		}

		if (_runTime > _bestTime)
		{
			_bestTime = _runTime;
		}

		SaveRecords();
	}

	private void UpdateCamera(float dt)
	{
		Vector3 target = new(_player.Position.X * 0.35f, 4.2f, -9.5f);
		_camera.Position = _camera.Position.Lerp(target, dt * 4f);
		_camera.LookAt(new Vector3(_player.Position.X * 0.2f, 1.0f, 10f), Vector3.Up);
	}

	private void UpdateHud()
	{
		_scoreLabel.Text = "Score: " + Mathf.FloorToInt(_score);
		_timeLabel.Text = "Time: " + FormatTime(_runTime);
		_bestLabel.Text = "Best Score: " + Mathf.FloorToInt(_bestScore) + "\nBest Time: " + FormatTime(_bestTime);

		_statusLabel.Text = _immuneTimer > 0f
			? "Shield: " + _immuneTimer.ToString("0.0") + "s"
			: _buffTimer > 0f
				? "Energy boost!"
				: _slowTimer > 0f
				? "Assignment drag!"
				: "Lane: " + _laneNames[_currentLane];

		_gameOverLabel.Text = _gameOver
			? "Game Over\nFinal Score: " + Mathf.FloorToInt(_score) + "\nSurvived: " + FormatTime(_runTime) + "\nPress an arrow key or R to restart."
			: "";
	}

	private void LoadRecords()
	{
		ConfigFile config = new();
		Error result = config.Load(SavePath);
		if (result != Error.Ok)
		{
			return;
		}

		_bestScore = (float)config.GetValue("records", "best_score", 0.0).AsDouble();
		_bestTime = (float)config.GetValue("records", "best_time", 0.0).AsDouble();
	}

	private void SaveRecords()
	{
		ConfigFile config = new();
		config.SetValue("records", "best_score", _bestScore);
		config.SetValue("records", "best_time", _bestTime);
		config.Save(SavePath);
	}

	private static MeshInstance3D CreateMesh(string name, Mesh mesh, Color color)
	{
		StandardMaterial3D material = new()
		{
			AlbedoColor = color,
			Roughness = 0.7f
		};

		if (color.A < 1f)
		{
			material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		}

		return new MeshInstance3D
		{
			Name = name,
			Mesh = mesh,
			MaterialOverride = material
		};
	}

	private static void AddPickupSprite(Node3D parent, PickupKind pickupKind)
	{
		string texturePath = pickupKind switch
		{
			PickupKind.EnergyDrink => "res://Art/energy_drink.svg",
			PickupKind.Snack => "res://Art/snack.svg",
			PickupKind.Homework => "res://Art/homework.svg",
			_ => "res://Art/project.svg"
		};

		Sprite3D sprite = new()
		{
			Name = pickupKind + " Icon",
			Texture = GD.Load<Texture2D>(texturePath),
			PixelSize = 0.012f,
			Position = new Vector3(0f, 1.05f, -0.05f),
			RotationDegrees = new Vector3(0f, 180f, 0f)
		};

		parent.AddChild(sprite);
	}

	private static float LaneToX(int lane)
	{
		return (lane - 1) * LaneWidth;
	}

	private static string FormatTime(float seconds)
	{
		int wholeSeconds = Mathf.FloorToInt(seconds);
		int minutes = wholeSeconds / 60;
		int remainderSeconds = wholeSeconds % 60;
		return minutes.ToString("00") + ":" + remainderSeconds.ToString("00");
	}
}
