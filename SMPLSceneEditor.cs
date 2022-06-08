global using System.Numerics;
global using SFML.Graphics;
global using SFML.Window;
global using SMPL;
global using SMPL.Tools;
global using Color = SFML.Graphics.Color;
using SFML.System;

namespace SMPLSceneEditor
{
	public partial class SMPLSceneEditor : Form
	{
		private readonly System.Windows.Forms.Timer loop;
		private readonly RenderWindow window;
		private float sceneSc = 1;
		private bool isDragSelecting, isHoveringScene;
		private Vector2 prevFormsMousePos, prevFormsMousePosGrid, selectStartPos, rightClickPos;
		private readonly List<string> selectedUIDs = new();

		public SMPLSceneEditor()
		{
			InitializeComponent();

			WindowState = FormWindowState.Maximized;

			window = new(windowSplit.Panel1.Handle);
			var view = window.GetView();
			view.Center = new();
			window.SetView(view);

			loop = new() { Interval = 1 };
			loop.Tick += OnUpdate;
			loop.Start();

			ResetView();
		}

		private void OnUpdate(object? sender, EventArgs e)
		{
			window.Size = new((uint)windowSplit.Panel1.Width, (uint)windowSplit.Panel1.Height);

			var view = window.GetView();
			view.Size = new(window.Size.X * sceneSc, window.Size.Y * sceneSc);
			window.SetView(view);

			window.Clear();
			DrawGrid();
			TryShowMousePos();

			TrySelect();

			ThingManager.UpdateAllThings();
			ThingManager.DrawAllVisuals(window);

			TryDrawSelection();

			window.Display();
		}

		private void DrawGrid()
		{
			if(gridThickness.Value == gridThickness.Minimum)
				return;

			var cellVerts = new VertexArray(PrimitiveType.Quads);
			var specialCellVerts = new VertexArray(PrimitiveType.Quads);
			var sz = new Vector2(windowSplit.Panel1.Width, windowSplit.Panel1.Height) * sceneSc;
			var thickness = (float)gridThickness.Value;
			var spacing = GetGridSpacing();
			var viewPos = window.GetView().Center;

			thickness *= sceneSc;
			thickness *= 0.5f;

			for(float i = 0; i <= sz.X * 4; i += spacing)
			{
				var x = viewPos.X - sz.X * 2 + i;
				var y = viewPos.Y;
				var top = new Vector2(x, y - sz.Y * 2).PointToGrid(new(spacing));
				var bot = new Vector2(x, y + sz.Y * 2).PointToGrid(new(spacing));
				var col = GetColor(top.X);
				var verts = GetVertexArray(top.X);

				verts.Append(new(top.PointMoveAtAngle(180, thickness, false).ToSFML(), col));
				verts.Append(new(top.PointMoveAtAngle(0, thickness, false).ToSFML(), col));
				verts.Append(new(bot.PointMoveAtAngle(0, thickness, false).ToSFML(), col));
				verts.Append(new(bot.PointMoveAtAngle(180, thickness, false).ToSFML(), col));
			}
			for(float i = 0; i <= sz.Y * 4; i += spacing)
			{
				var x = viewPos.X;
				var y = viewPos.Y - sz.Y * 2 + i;
				var left = new Vector2(x - sz.X * 2, y).PointToGrid(new(spacing));
				var right = new Vector2(x + sz.X * 2, y).PointToGrid(new(spacing));
				var col = GetColor(left.Y);
				var verts = GetVertexArray(left.Y);

				verts.Append(new(left.PointMoveAtAngle(270, thickness, false).ToSFML(), col));
				verts.Append(new(left.PointMoveAtAngle(90, thickness, false).ToSFML(), col));
				verts.Append(new(right.PointMoveAtAngle(90, thickness, false).ToSFML(), col));
				verts.Append(new(right.PointMoveAtAngle(270, thickness, false).ToSFML(), col));
			}

			window.Draw(cellVerts);
			window.Draw(specialCellVerts);

			Color GetColor(float coordinate)
			{
				if(coordinate == 0)
					return SFML.Graphics.Color.Yellow;
				else if(coordinate % 1000 == 0)
					return SFML.Graphics.Color.White;

				return new SFML.Graphics.Color(50, 50, 50);
			}
			VertexArray GetVertexArray(float coordinate)
			{
				return coordinate == 0 || coordinate % 1000 == 0 ? specialCellVerts : cellVerts;
			}
		}
		private void TryShowMousePos()
		{
			if(sceneMousePos.Visible == false)
				return;

			var gridSpacing = GetGridSpacing();
			var mousePos = GetMousePosition();
			var inGrid = mousePos.PointToGrid(new(gridSpacing)) + new Vector2(gridSpacing) * 0.5f;
			sceneMousePos.Text =
				$"Cursor [{(int)mousePos.X} {(int)mousePos.Y}]\n" +
				$"Grid [{(int)inGrid.X} {(int)inGrid.Y}]";
		}

		private Hitbox GetDragSelectionHitbox()
		{
			var mousePos = Mouse.GetPosition(window);
			var topLeft = new Vector2i((int)selectStartPos.X, (int)selectStartPos.Y);
			var botRight = new Vector2i(mousePos.X, mousePos.Y);
			var topRight = new Vector2i(botRight.X, topLeft.Y);
			var botLeft = new Vector2i(topLeft.X, botRight.Y);
			var tl = window.MapPixelToCoords(topLeft).ToSystem();
			var tr = window.MapPixelToCoords(topRight).ToSystem();
			var br = window.MapPixelToCoords(botRight).ToSystem();
			var bl = window.MapPixelToCoords(botLeft).ToSystem();

			return new Hitbox(tl, tr, br, bl, tl);
		}
		private void TrySelect()
		{
			var left = Mouse.IsButtonPressed(Mouse.Button.Left);
			var click = left.Once("leftClick");

			if(isHoveringScene == false)
				return;

			var mousePos = GetMousePosition();
			var rawMousePos = Mouse.GetPosition(window);
			var uids = ThingManager.GetUIDs();
			var dragSelHitbox = GetDragSelectionHitbox();
			var clickedUIDs = new SortedDictionary<int, string>();
			var dist = selectStartPos.DistanceBetweenPoints(new(rawMousePos.X, rawMousePos.Y));

			if(click)
			{
				selectedUIDs.Clear();
				for(int i = 0; i < uids.Count; i++)
				{
					var uid = uids[i];
					var hitbox = GetHitbox(uid);
					if(hitbox == null || ThingManager.HasGet(uid, "Depth") == false)
						continue;

					TryTransformHitbox(uid);
					if(hitbox.ConvexContains(mousePos))
						clickedUIDs[(int)ThingManager.Get(uid, "Depth")] = uid;
				}
			}
			else if(left && dist > 1)
			{
				selectedUIDs.Clear();
				for(int i = 0; i < uids.Count; i++)
				{
					var uid = uids[i];
					var hitbox = GetHitbox(uid);
					if(hitbox == null)
						continue;

					TryTransformHitbox(uid);

					if(dragSelHitbox != null && dragSelHitbox.ConvexContains(hitbox))
						selectedUIDs.Add(uid);
				}
			}

			foreach(var kvp in clickedUIDs)
			{
				selectedUIDs.Add(kvp.Value);
				break;
			}
		}
		private void TryDrawSelection()
		{
			for(int i = 0; i < selectedUIDs.Count; i++)
				Draw((Hitbox)ThingManager.Get(selectedUIDs[i], "Hitbox"));

			if(isDragSelecting)
				Draw(GetDragSelectionHitbox());

			void Draw(Hitbox? hitbox)
			{
				if(hitbox == null)
					return;

				var topL = hitbox.Lines[0].A;
				var topR = hitbox.Lines[0].B;
				var botR = hitbox.Lines[1].B;
				var botL = hitbox.Lines[2].B;
				var fillCol = new Color(0, 180, 255, 100);
				var outCol = Color.White;
				var fill = new Vertex[]
				{
					new(topL.ToSFML(), fillCol),
					new(topR.ToSFML(), fillCol),
					new(botR.ToSFML(), fillCol),
					new(botL.ToSFML(), fillCol),
				};

				new Line(topL, topR).Draw(window, outCol);
				new Line(topR, botR).Draw(window, outCol);
				new Line(botR, botL).Draw(window, outCol);
				new Line(botL, topL).Draw(window, outCol);

				window.Draw(fill, PrimitiveType.Quads);
			}
		}

		private static Hitbox? GetHitbox(string uid)
		{
			return ThingManager.HasGet(uid, "Hitbox") == false ? default : (Hitbox)ThingManager.Get(uid, "Hitbox");
		}
		private Vector2 GetFormsMousePos()
		{
			var view = window.GetView();
			var scale = view.Size.ToSystem() / new Vector2(windowSplit.Panel1.Width, windowSplit.Panel1.Height);
			return new Vector2(MousePosition.X, MousePosition.Y) * scale;
		}
		private float GetGridSpacing()
		{
			return MathF.Max(gridSpacing.Text.ToNumber(), 8);
		}
		private Vector2 GetMousePosition()
		{
			var mp = Mouse.GetPosition(window);
			var mp2 = window.MapPixelToCoords(new(mp.X, mp.Y), window.GetView());
			return new Vector2(mp2.X, mp2.Y);
		}
		private void UpdateZoom()
		{
			sceneSc = ((float)sceneZoom.Value).Map(0, 100, 0.1f, 10f);
		}
		private void ResetView()
		{
			sceneAngle.Value = 0;
			sceneZoom.Value = 10;
			var view = window.GetView();
			view.Center = new();
			view.Rotation = 0;
			sceneSc = 1;
			UpdateZoom();
		}
		private Vector2 Drag(Vector2 point, bool reverse = false, bool snapToGrid = false)
		{
			var view = window.GetView();
			var prev = snapToGrid ? prevFormsMousePosGrid : prevFormsMousePos;
			var pos = GetFormsMousePos();
			var gridSp = new Vector2(GetGridSpacing());

			if(snapToGrid)
				pos = pos.PointToGrid(gridSp) + gridSp * 0.5f;

			var dist = prev.DistanceBetweenPoints(pos);
			var ang = prev.AngleBetweenPoints(pos);

			if(reverse)
				dist *= -1;

			return dist == 0 ? point : point.PointMoveAtAngle(view.Rotation + ang, dist, false);
		}
		private static void TryTransformHitbox(string uid)
		{
			if(ThingManager.HasGet(uid, "Hitbox") == false)
				return;

			var hitbox = (Hitbox)ThingManager.Get(uid, "Hitbox");
			hitbox.TransformLocalLines(uid);
		}

		private void OnMouseLeaveScene(object sender, EventArgs e)
		{
			isHoveringScene = false;
			sceneMousePos.Hide();
		}
		private void OnMouseEnterScene(object sender, EventArgs e)
		{
			isHoveringScene = true;
			sceneMousePos.Show();
		}
		private void OnMouseMoveScene(object sender, MouseEventArgs e)
		{
			if(e.Button == MouseButtons.Middle)
			{
				if(selectedUIDs.Count == 0)
				{
					var view = window.GetView();
					view.Center = Drag(view.Center.ToSystem(), true).ToSFML();
					window.SetView(view);
				}
				else
				{
					for(int i = 0; i < selectedUIDs.Count; i++)
					{
						var uid = selectedUIDs[i];
						var pos = (Vector2)ThingManager.Get(uid, "Position");

						ThingManager.Set(uid, "Position", Drag(pos, false, gridSnap.Checked));
						TryTransformHitbox(uid);
					}
				}

				System.Windows.Forms.Cursor.Current = Cursors.NoMove2D;
			}

			var gridSp = new Vector2(GetGridSpacing());
			prevFormsMousePos = GetFormsMousePos();
			prevFormsMousePosGrid = prevFormsMousePos.PointToGrid(gridSp) + gridSp * 0.5f;
		}
		private void OnSceneZoom(object sender, EventArgs e)
		{
			UpdateZoom();
		}
		private void OnSceneRotate(object sender, EventArgs e)
		{
			var view = window.GetView();
			view.Rotation = ((float)sceneAngle.Value).Map(0, 100, 0, 360);
			window.SetView(view);
		}
		private void OnMouseDownScene(object sender, MouseEventArgs e)
		{
			if(e.Button != MouseButtons.Left)
				return;

			windowSplit.Panel1.Focus();

			isDragSelecting = true;
			var pos = Mouse.GetPosition(window);
			selectStartPos = new(pos.X, pos.Y);
		}
		private void OnMouseUpScene(object sender, MouseEventArgs e)
		{
			if(e.Button == MouseButtons.Right)
				rightClickPos = GetMousePosition();

			if(e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
				return;

			isDragSelecting = false;
		}
		private void OnKeyDownTopLeftTabs(object sender, System.Windows.Forms.KeyEventArgs e)
		{
			//Hotkey.TryTriggerHotkeys();
		}
		private void OnSceneStatusClick(object sender, EventArgs e)
		{
			windowSplit.Panel1.Focus();
		}

		private void OnSceneRightClickMenuResetView(object sender, EventArgs e)
		{
			ResetView();
		}
		private void OnSceneRightClickMenuCreateSprite(object sender, EventArgs e)
		{
			var uid = ThingManager.CreateSprite("sprite");
			ThingManager.Set(uid, "Position", rightClickPos);
			ThingManager.Set(uid, "Tint", Color.Blue);
			ThingManager.Do(uid, "ApplyDefaultHitbox");
			TryTransformHitbox(uid);
			selectedUIDs.Add(uid);
		}
	}
}