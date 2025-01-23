using System;
using System.Drawing;
using System.Windows.Forms;
using MediaToolkit;
using MediaToolkit.Model;
using MediaToolkit.Options;
using Microsoft.EntityFrameworkCore;
using OrganizadorVideos.Models;
using System.IO;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using System.Collections.Generic;
using System.Linq;
using OrganizadorVideos.Data;
using OrganizadorVideos.Forms;

namespace OrganizadorVideos
{
    public partial class Form1 : Form
    {
        private FlowLayoutPanel flowLayoutPanel;
        private Point selectionStart;
        private bool isSelecting;
        private List<Panel> selectedPanels = new List<Panel>();
        private Rectangle rubberBandRect;

        public Form1()
        {

            ConfigureUI();
            CargarVideosDesdeDB();
        }

        private void ConfigureUI()
        {
            // Configure main window
            this.Text = "Organizador de Videos";
            this.Size = new Size(800, 600);

            // Create top panel for buttons
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50
            };
            this.Controls.Add(topPanel);

            // Add video selection button with icon
            var btnAddVideo = new Button
            {
                Size = new Size(40, 40),
                Location = new Point(5, 5),
                BackgroundImage = Image.FromFile(Path.Combine("iconos", "add-video.png")),
                BackgroundImageLayout = ImageLayout.Zoom,
                Text = "",
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(5) // Add padding
            };
            btnAddVideo.Click += BtnAddVideo_Click;
            topPanel.Controls.Add(btnAddVideo);

            // Add folder selection button
            var btnAddFolder = new Button
            {
                Size = new Size(40, 40),
                Location = new Point(50, 5),  // Position to right of first button
                BackgroundImage = Image.FromFile(Path.Combine("iconos", "add-folder.png")),
                BackgroundImageLayout = ImageLayout.Zoom,
                Text = "",
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(5) // Add padding
            };
            btnAddFolder.Click += BtnAddFolder_Click;
            topPanel.Controls.Add(btnAddFolder);

            // Create flow panel for thumbnails
            flowLayoutPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.White
            };
            this.Controls.Add(flowLayoutPanel);

            flowLayoutPanel.MouseDown += FlowLayoutPanel_MouseDown;
            flowLayoutPanel.MouseMove += FlowLayoutPanel_MouseMove;
            flowLayoutPanel.MouseUp += FlowLayoutPanel_MouseUp;
            flowLayoutPanel.Paint += FlowLayoutPanel_Paint;
        }

        private void BtnAddVideo_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Archivos de video|*.mp4;*.avi;*.mkv;*.mov|Todos los archivos|*.*";
                openFileDialog.Title = "Seleccionar un video";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    AddVideoThumbnail(openFileDialog.FileName);
                }
            }
        }

        private void BtnAddFolder_Click(object sender, EventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    var carpeta = folderDialog.SelectedPath;
                    var extensionesPermitidas = new[] { ".mp4", ".avi", ".mkv", ".mov" };
                    var archivos = Directory.GetFiles(carpeta)
                        .Where(f => extensionesPermitidas.Contains(Path.GetExtension(f).ToLower()))
                        .ToList();

                    foreach (var archivo in archivos)
                    {
                        AddVideoThumbnail(archivo);
                    }
                }
            }
        }

        private void AddVideoThumbnail(string videoPath)
        {
            // Create permanent thumbnail directory
            var thumbDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OrganizadorVideos",
                "Thumbnails"
            );
            Directory.CreateDirectory(thumbDir);

            // Generate permanent thumbnail path
            var thumbName = $"{Path.GetFileNameWithoutExtension(videoPath)}_{Guid.NewGuid()}.jpg";
            var thumbPath = Path.Combine(thumbDir, thumbName);

            // Generate thumbnail
            using (var engine = new Engine())
            {
                var inputFile = new MediaFile { Filename = videoPath };
                var outputFile = new MediaFile { Filename = thumbPath };
                var options = new ConversionOptions { Seek = TimeSpan.FromSeconds(0) };
                engine.GetThumbnail(inputFile, outputFile, options);
            }

            // Save to database
            using (var context = new AppDbContext())
            {
                var video = new Video
                {
                    Nombre = Path.GetFileName(videoPath),
                    RutaVideo = videoPath,
                    RutaMiniatura = thumbPath,
                    FechaCarga = DateTime.Now
                };

                context.Videos.Add(video);
                context.SaveChanges();
            }

            // ...existing UI code for panel, pictureBox, etc...
            var panel = new Panel
            {
                Width = 150,
                Height = 165, // Reduced height
                Margin = new Padding(5),
                BackColor = Color.White,
                Padding = new Padding(0) // Remove internal padding
            };

            var pictureBox = new PictureBox
            {
                Width = 150,
                Height = 150,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = Image.FromFile(thumbPath),
                Margin = new Padding(0),
                Location = new Point(0, 0), // Explicitly position at top
                Tag = videoPath // Store video path for reference
            };

            // Add context menu
            AddContextMenu(panel, thumbPath, videoPath);

            // Remove single click handler, keep only double click for video playback
            pictureBox.DoubleClick += (sender, e) =>
            {
                var playerForm = new VideoPlayerForm();
                playerForm.PlayVideo(videoPath);
                playerForm.Show();
            };

            pictureBox.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if ((ModifierKeys & Keys.Control) == Keys.Control)
                    {
                        if (selectedPanels.Contains(panel))
                        {
                            selectedPanels.Remove(panel);
                            panel.BackColor = Color.White;
                        }
                        else
                        {
                            SelectPanel(panel);
                        }
                    }
                    else if (!selectedPanels.Contains(panel))
                    {
                        ClearSelection();
                        SelectPanel(panel);
                    }
                }
            };

            var label = new Label
            {
                Text = Path.GetFileName(videoPath),
                AutoEllipsis = true,
                Width = 150,
                Height = 15, // Reduced height
                TextAlign = ContentAlignment.TopLeft,
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Margin = new Padding(0), // Remove margin
                Padding = new Padding(2, 0, 2, 0), // Small horizontal padding
                Location = new Point(0, 150) // Position directly below PictureBox
            };

            var toolTip = new ToolTip();
            toolTip.SetToolTip(label, Path.GetFileName(videoPath));

            panel.Controls.Add(pictureBox);
            panel.Controls.Add(label);
            flowLayoutPanel.Controls.Add(panel);
        }

        private void EliminarDelPrograma(Panel panel, string thumbPath, string videoPath)
        {
            if (MessageBox.Show("¿Está seguro de eliminar este video del programa?",
                "Confirmar eliminación", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                try
                {
                    // Dispose image before deletion
                    var pictureBox = panel.Controls.OfType<PictureBox>().FirstOrDefault();
                    if (pictureBox != null && pictureBox.Image != null)
                    {
                        var image = pictureBox.Image;
                        pictureBox.Image = null;
                        image.Dispose();
                    }

                    // Remove from database
                    using (var context = new AppDbContext())
                    {
                        var video = context.Videos.FirstOrDefault(v => v.RutaVideo == videoPath);
                        if (video != null)
                        {
                            context.Videos.Remove(video);
                            context.SaveChanges();
                        }
                    }

                    // Delete thumbnail file
                    if (File.Exists(thumbPath))
                    {
                        File.Delete(thumbPath);
                    }

                    // Remove from UI
                    flowLayoutPanel.Controls.Remove(panel);
                    panel.Dispose();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al eliminar: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void EliminarDelProgramaSinConfirmacion(Panel panel, string thumbPath, string videoPath)
        {
            try
            {
                // Dispose image before deletion
                var pictureBox = panel.Controls.OfType<PictureBox>().FirstOrDefault();
                if (pictureBox?.Image != null)
                {
                    var image = pictureBox.Image;
                    pictureBox.Image = null;
                    image.Dispose();
                }

                // Remove from database
                using (var context = new AppDbContext())
                {
                    var video = context.Videos.FirstOrDefault(v => v.RutaVideo == videoPath);
                    if (video != null)
                    {
                        context.Videos.Remove(video);
                        context.SaveChanges();
                    }
                }

                // Delete thumbnail file
                if (File.Exists(thumbPath))
                {
                    File.Delete(thumbPath);
                }

                // Remove from UI
                flowLayoutPanel.Controls.Remove(panel);
                panel.Dispose();
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al eliminar {Path.GetFileName(videoPath)}: {ex.Message}");
            }
        }

        private void BorradoDefinitivo(Panel panel, string thumbPath, string videoPath)
        {
            if (MessageBox.Show(
                "¿Está seguro de eliminar definitivamente este video?\n" +
                "Esta acción eliminará el archivo de video original y no se podrá recuperar.",
                "Confirmar borrado definitivo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                try
                {
                    // Dispose image before deletion
                    var pictureBox = panel.Controls.OfType<PictureBox>().FirstOrDefault();
                    if (pictureBox != null && pictureBox.Image != null)
                    {
                        var image = pictureBox.Image;
                        pictureBox.Image = null;
                        image.Dispose();
                    }

                    // Remove from database
                    using (var context = new AppDbContext())
                    {
                        var video = context.Videos.FirstOrDefault(v => v.RutaVideo == videoPath);
                        if (video != null)
                        {
                            context.Videos.Remove(video);
                            context.SaveChanges();
                        }
                    }

                    // Delete thumbnail file
                    if (File.Exists(thumbPath))
                    {
                        File.Delete(thumbPath);
                    }

                    // Delete original video file
                    if (File.Exists(videoPath))
                    {
                        File.Delete(videoPath);
                    }

                    // Remove from UI
                    flowLayoutPanel.Controls.Remove(panel);
                    panel.Dispose();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al eliminar: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void CargarVideosDesdeDB()
        {
            try
            {
                using (var context = new AppDbContext())
                {
                    if (context.Database.CanConnect() && context.Videos != null)
                    {
                        var videos = context.Videos.ToList();
                        foreach (var video in videos)
                        {
                            if (File.Exists(video.RutaVideo) && File.Exists(video.RutaMiniatura))
                            {
                                AddVideoThumbnailFromDB(video);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar videos: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddVideoThumbnailFromDB(Video video)
        {
            var panel = new Panel
            {
                Width = 150,
                Height = 165,
                Margin = new Padding(5),
                BackColor = Color.White,
                Padding = new Padding(0)
            };

            var pictureBox = new PictureBox
            {
                Width = 150,
                Height = 150,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = Image.FromFile(video.RutaMiniatura),
                Margin = new Padding(0),
                Location = new Point(0, 0)
            };

            // Add context menu
            AddContextMenu(panel, video.RutaMiniatura, video.RutaVideo);

            // Remove single click handler, keep only double click for video playback
            pictureBox.DoubleClick += (sender, e) =>
            {
                var playerForm = new VideoPlayerForm();
                playerForm.PlayVideo(video.RutaVideo);
                playerForm.Show();
            };

            pictureBox.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if ((ModifierKeys & Keys.Control) == Keys.Control)
                    {
                        if (selectedPanels.Contains(panel))
                        {
                            selectedPanels.Remove(panel);
                            panel.BackColor = Color.White;
                        }
                        else
                        {
                            SelectPanel(panel);
                        }
                    }
                    else if (!selectedPanels.Contains(panel))
                    {
                        ClearSelection();
                        SelectPanel(panel);
                    }
                }
            };

            var label = new Label
            {
                Text = video.Nombre,
                AutoEllipsis = true,
                Width = 150,
                Height = 15,
                TextAlign = ContentAlignment.TopLeft,
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Margin = new Padding(0),
                Padding = new Padding(2, 0, 2, 0),
                Location = new Point(0, 150)
            };

            var toolTip = new ToolTip();
            toolTip.SetToolTip(label, video.Nombre);

            panel.Controls.Add(pictureBox);
            panel.Controls.Add(label);
            flowLayoutPanel.Controls.Add(panel);
        }

        private void FlowLayoutPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                selectionStart = flowLayoutPanel.PointToClient(Cursor.Position);
                isSelecting = true;
                rubberBandRect = Rectangle.Empty;

                if ((ModifierKeys & Keys.Control) != Keys.Control)
                {
                    ClearSelection();
                }

                flowLayoutPanel.Invalidate();
            }
        }

        private void FlowLayoutPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (isSelecting)
            {
                var currentPoint = flowLayoutPanel.PointToClient(Cursor.Position);

                rubberBandRect = new Rectangle(
                    Math.Min(currentPoint.X, selectionStart.X),
                    Math.Min(currentPoint.Y, selectionStart.Y),
                    Math.Abs(currentPoint.X - selectionStart.X),
                    Math.Abs(currentPoint.Y - selectionStart.Y)
                );

                // Update selection without moving controls
                foreach (Panel panel in flowLayoutPanel.Controls.OfType<Panel>())
                {
                    if (rubberBandRect.IntersectsWith(panel.Bounds))
                    {
                        SelectPanel(panel);
                    }
                    else if (!ModifierKeys.HasFlag(Keys.Control))
                    {
                        panel.BackColor = Color.White;
                        selectedPanels.Remove(panel);
                    }
                }

                flowLayoutPanel.Invalidate();
            }
        }

        private void FlowLayoutPanel_MouseUp(object sender, MouseEventArgs e)
        {
            if (isSelecting)
            {
                isSelecting = false;
                rubberBandRect = Rectangle.Empty;
                flowLayoutPanel.Invalidate();
            }
        }

        private void FlowLayoutPanel_Paint(object sender, PaintEventArgs e)
        {
            if (isSelecting && !rubberBandRect.IsEmpty)
            {
                using (var brush = new SolidBrush(Color.FromArgb(128, 173, 216, 230)))
                {
                    e.Graphics.FillRectangle(brush, rubberBandRect);
                }
                using (var pen = new Pen(Color.FromArgb(173, 216, 230)))
                {
                    e.Graphics.DrawRectangle(pen, rubberBandRect);
                }
            }
        }

        private void SelectPanel(Panel panel)
        {
            if (!selectedPanels.Contains(panel))
            {
                selectedPanels.Add(panel);
                panel.BackColor = Color.LightBlue;
            }
        }

        private void ClearSelection()
        {
            foreach (var panel in selectedPanels)
            {
                panel.BackColor = Color.White;
            }
            selectedPanels.Clear();
        }

        private void AddContextMenu(Panel panel, string thumbPath, string videoPath)
        {
            var contextMenu = new ContextMenuStrip();
            var eliminarItem = new ToolStripMenuItem("Eliminar del programa");
            eliminarItem.Click += (s, e) =>
            {
                if (selectedPanels.Count > 1 && selectedPanels.Contains(panel))
                {
                    EliminarSeleccionadosDelPrograma();
                }
                else
                {
                    EliminarDelPrograma(panel, thumbPath, videoPath);
                }
            };

            var borradoDefinitivo = new ToolStripMenuItem("Borrado definitivo");
            borradoDefinitivo.Click += (s, e) =>
            {
                if (selectedPanels.Count > 1 && selectedPanels.Contains(panel))
                {
                    BorradoDefinitivoSeleccionados();
                }
                else
                {
                    BorradoDefinitivo(panel, thumbPath, videoPath);
                }
            };

            contextMenu.Items.AddRange(new ToolStripItem[] { eliminarItem, borradoDefinitivo });
            panel.ContextMenuStrip = contextMenu;
        }

        private void EliminarSeleccionadosDelPrograma()
        {
            if (selectedPanels == null || selectedPanels.Count == 0) return;

            if (MessageBox.Show(
                $"¿Está seguro de eliminar {selectedPanels.Count} videos del programa?",
                "Confirmar eliminación múltiple",
                MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                flowLayoutPanel.SuspendLayout();

                // Create copy and reverse to avoid index issues
                var panelsToDelete = selectedPanels.ToList();
                panelsToDelete.Reverse();

                foreach (var panel in panelsToDelete)
                {
                    string videoPath = string.Empty;
                    string thumbPath = string.Empty;

                    try
                    {
                        var pictureBox = panel.Controls.OfType<PictureBox>().FirstOrDefault();
                        if (pictureBox?.Tag == null) continue;

                        videoPath = pictureBox.Tag.ToString();
                        thumbPath = GetThumbnailPath(videoPath);

                        // 1. Remove from UI first
                        flowLayoutPanel.Controls.Remove(panel);
                        selectedPanels.Remove(panel);

                        // 2. Clean up image resources
                        if (pictureBox.Image != null)
                        {
                            var image = pictureBox.Image;
                            pictureBox.Image = null;
                            image.Dispose();
                        }

                        // 3. Remove from database
                        using (var context = new AppDbContext())
                        {
                            var video = context.Videos.FirstOrDefault(v => v.RutaVideo == videoPath);
                            if (video != null)
                            {
                                context.Videos.Remove(video);
                                context.SaveChanges();
                            }
                        }

                        // 4. Delete thumbnail file
                        if (File.Exists(thumbPath))
                        {
                            File.Delete(thumbPath);
                        }

                        panel.Dispose();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error al eliminar {Path.GetFileName(videoPath)}: {ex.Message}");
                    }
                }

                selectedPanels.Clear();
                flowLayoutPanel.ResumeLayout(true);
                flowLayoutPanel.Refresh();
            }
        }

        private void BorradoDefinitivoSeleccionados()
        {
            if (MessageBox.Show(
                $"¿Está seguro de eliminar definitivamente {selectedPanels.Count} videos?\n" +
                "Esta acción eliminará los archivos de video originales y no se podrán recuperar.",
                "Confirmar borrado definitivo múltiple",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                var panelsToDelete = selectedPanels.ToList(); // Create copy to avoid modification during iteration
                foreach (var panel in panelsToDelete)
                {
                    var pictureBox = panel.Controls.OfType<PictureBox>().FirstOrDefault();
                    if (pictureBox != null)
                    {
                        var videoPath = pictureBox.Tag.ToString();
                        var thumbPath = GetThumbnailPath(videoPath);
                        BorradoDefinitivo(panel, thumbPath, videoPath);
                    }
                }
            }
        }

        private string GetThumbnailPath(string videoPath)
        {
            using (var context = new AppDbContext())
            {
                var video = context.Videos.FirstOrDefault(v => v.RutaVideo == videoPath);
                return video?.RutaMiniatura;
            }
        }
    }
}