using SixLabors.ImageSharp;
using System.Collections.Concurrent;

namespace OOCL.Core
{
    public class ImageCollection : IDisposable
    {
		private readonly ConcurrentDictionary<Guid, ImageObj> images = [];
        private readonly object lockObj = new();

        public IReadOnlyCollection<ImageObj> Images => this.images.Values.ToList();

        public ImageObj? this[Guid guid]
        {
            get
            {
                this.images.TryGetValue(guid, out ImageObj? imgObj);
                return imgObj;
            }
        }

        public ImageObj? this[string name]
        {
            get
            {
                lock (this.lockObj)
                {
                    return this.images.Values.FirstOrDefault(img => img.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        public ImageObj? this[int index]
        {
            get
            {
                lock (this.lockObj)
                {
                    return this.images.Values.ElementAtOrDefault(index);
                }
            }
        }

		// Options
		public string ImportPath { get; set; } = string.Empty;
		public string ExportPath { get; set; } = string.Empty;
		public bool SaveMemory { get; set; } = false;
        public int DefaultWidth { get; set; } = 720;
        public int DefaultHeight { get; set; } = 480;

		// Ctor with options
		public ImageCollection(bool saveMemory = false, int defaultWidth = 720, int defaultHeight = 480)
        {
            this.DefaultWidth = Math.Max(defaultWidth, 360); // Min is 360px width
			this.DefaultHeight = Math.Max(defaultHeight, 240); // Min is 240px height
			this.SaveMemory = saveMemory;
            if (this.SaveMemory)
            {
                Console.WriteLine("ImageCollection: Memory saving enabled. All images will be disposed on add.");
            }
		}

		public bool Add(ImageObj imgObj)
        {
			if (this.SaveMemory)
            {
                // Dispose every image
                lock (this.lockObj)
                {
                    foreach (var i in this.images.Values)
                    {
                        i.Dispose();
                    }
                   
                    this.images.Clear();
				}
			}
			
            return this.images.TryAdd(imgObj.Id, imgObj);
        }

        public bool Remove(Guid guid)
        {
            bool result = this.images.TryRemove(guid, out ImageObj? imgObj);
            if (result && imgObj != null)
            {
                imgObj.Dispose();
                Console.WriteLine($"Removed and disposed image '{imgObj.Name}' (ID: {imgObj.Id}).");
            }
            else
            {
                Console.WriteLine($"Failed to remove image with ID: {guid}. It might not exist.");
			}

            return result;
		}

        public async Task Clear()
        {
            await Task.Run(() =>
            {
                lock (this.lockObj)
                {
                    foreach (var imgObj in this.images.Values)
                    {
                        imgObj.Dispose();
                        Console.WriteLine($"Disposed image '{imgObj.Name}' (Guid: {imgObj.Id}).");
                    }

                    this.images.Clear();
                }
            });
		}

        public void Dispose()
        {
            this.Clear().Wait();
            GC.SuppressFinalize(this);
        }

        public async Task<ImageObj?> LoadImage(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"LoadImage: File not found or path empty: {filePath}");
                return null;
            }

            ImageObj obj;

			try
            {
				obj = await Task.Run(() =>
				{
					return new ImageObj(filePath);
				});
			}
            catch (Exception ex)
            {
                try
                {
                    obj = new ImageObj(filePath);
                }
                catch (Exception innerEx)
                {
                    Console.WriteLine($"Error creating ImageObj from file '{filePath}': {innerEx.Message}");
                    return null;
                }

				Console.WriteLine($"Error loading image from file '{filePath}': {ex.Message}");
                return null;
			}

			if (this.Add(obj))
            {
                Console.WriteLine($"Loaded and added image '{obj.Name}' (ID: {obj.Id}) from file.");
                return obj;
            }

			// obj.Dispose();
			Console.WriteLine($"Failed to add image '{obj.Name}' (ID: {obj.Id}). An image with this ID might already exist.");
			return null;
		}

        public async Task<ImageObj?> PopEmpty(Size? size = null, bool add = false)
        {
            size ??= new Size(1080, 1920);
            int number = this.images.Count + 1;
            int digits = (int)Math.Log10(number) + 1;

            ImageObj imgObj;

            try
            {
                imgObj = await Task.Run(() =>
                {
                     lock (this.lockObj)
                    {
                        return new ImageObj(new byte[size.Value.Width * size.Value.Height * 4], size.Value.Width, size.Value.Height, $"EmptyImage_{number.ToString().PadLeft(digits, '0')}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating empty image: {ex.Message}");
                return null;
            }

			if (add)
            {
				if (this.Add(imgObj))
				{
					Console.WriteLine($"Created and added empty image '{imgObj.Name}' (ID: {imgObj.Id}) with size {size.Value.Width}x{size.Value.Height}.");
					return imgObj;
				}

				imgObj.Dispose();
				Console.WriteLine($"Failed to add empty image '{imgObj.Name}' (ID: {imgObj.Id}). An image with this ID might already exist.");
				return null;
			}

            Console.WriteLine($"Created empty image '{imgObj.Name}' (ID: {imgObj.Id}) with size {size.Value.Width}x{size.Value.Height}, but not added to collection.");
            return imgObj;
		}

        public async Task<string?> ExportImage(Guid guid, string? exportPath = null, string format = "png")
        {
            exportPath ??= this.ExportPath;
            ImageObj? obj = this[guid];
            if (obj != null)
            {
                return await obj.Export(exportPath, format);
            }

            return null;
		}
	}
}