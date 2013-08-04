using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing.Imaging;
using System.Drawing;

namespace SeamCarving
{

    class SeamCarving
    {
        /// <summary>
        /// Klasse, die einen vertikalen Seam repräsentiert
        /// </summary>
        class VerticalSeam
        {
            //Die X-Positionen des Seams
            public int[] xPositions;
            //Die Farbe des Bildes entlang des Seams
            public Color[] srcColors;

            public VerticalSeam(int len)
            {
                xPositions = new int[len];
                srcColors = new Color[len];
            }
        }

        //Abmessungen des Ausgangsbildes
        private int srcWidth;
        private int srcHeight;

        public int SourceWidth { get { return srcWidth; } }
        public int SourceHeight { get { return srcHeight; } }

        //Abmessungen des aktuell veränderten Bildes
        private int currentWidth;
        private int currentHeight;

        public int CurrentWidth { get { return currentWidth; } }
        public int CurrentHeight { get { return currentHeight; } }

        //Buffer für das EnergieBild
        private int[] energyMap;

        //Buffer für die Seam-Findung
        private int[] flowMap;

        //temporäres Ausgabebild
        private Bitmap tempImg;

        //Liste der entfernten Seams
        private LinkedList<VerticalSeam> seams;

        public SeamCarving(Bitmap img)
        {
            this.srcWidth = img.Width;
            this.srcHeight = img.Height;

            this.energyMap = new int[srcWidth * srcHeight];
            this.flowMap = new int[srcWidth * srcHeight];
            this.tempImg = new Bitmap(srcWidth, srcHeight, PixelFormat.Format24bppRgb);

            SetSourceImage(img);
        }


        /// <summary>
        /// Methode um mehrere Seams zu entfernen
        /// </summary>
        /// <param name="seamCount">Die Anzahl der zu entfernenden Seams</param>
        /// <returns>Das verkleinerte Bild</returns>
        public Bitmap RemoveSeams(int seamCount)
        {
            //Parameter gegebenenfalls korrigieren
            if (currentWidth - seamCount <= 0) seamCount = 0;

            for (int i = 0; i < seamCount; i++)
            {
                //zu entfernenden Seam ermitteln
                VerticalSeam seam = FindSeam();

                //in der Historie sichern
                seams.AddLast(seam);

                //Aus dem Bild entfernen
                RemoveSeam(seam);

                //Energiebild aktualisieren
                FillEnergyMap(EnergyMapCalculationType.PartialByShrinking, seam);

                //Buffer für die effiziente Seam-Findung aktualisieren
                FillFlowMap();
            }

            //verkleinertes Bitmap erzeugen
            Bitmap result = CreateBitmap();

            return result;
        }


        /// <summary>
        /// Methode um mehrere Seams wieder einzufügen
        /// </summary>
        /// <param name="seamCount">Die Anzahl der wieder einzufügenden Seams</param>
        /// <returns>Das vergrößerte Bild</returns>
        public Bitmap InsertSeams(int seamCount)
        {
            if (currentWidth + seamCount > srcWidth) seamCount = 0;

            for (int i = 0; i < seamCount; i++)
            {
                //zuletzt enttfernten Seam holen
                VerticalSeam seam = seams.Last.Value;
                seams.RemoveLast();

                //Seam wieder in das Bild einfügen
                InsertSeam(seam);

                //Energiebild aktualisieren
                FillEnergyMap(EnergyMapCalculationType.PartialByWidening, seam);

                //Buffer für die effiziente Seam-Findung aktualisieren
                FillFlowMap();
            }

            //vergrößertes Bitmap erzeugen
            Bitmap result = CreateBitmap();

            return result;
        }



        /// <summary>
        /// Methode zum (er)setzen des Ausgangsbildes
        /// </summary>
        /// <param name="inImg">Ausgangsbild</param>
        unsafe public void SetSourceImage(Bitmap inImg)
        {
            //Abmessungen des Bildes checken
            if (inImg == null) throw new ArgumentNullException();
            if (inImg.PixelFormat != PixelFormat.Format24bppRgb) throw new ArgumentException();
            if (inImg.Height != srcHeight) throw new ArgumentException();
            if (inImg.Width != srcWidth) throw new ArgumentException();

            //Bitmaps locken
            BitmapData inBitmapData = inImg.LockBits(new Rectangle(new Point(0, 0), inImg.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            BitmapData tempBitmapData = tempImg.LockBits(new Rectangle(new Point(0, 0), tempImg.Size), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            //Bildinhalt kopieren
            for (int y = 0; y < srcHeight; y++)
            {
                byte* inScanLine = (byte*)inBitmapData.Scan0.ToPointer() + y * inBitmapData.Stride;
                byte* tempScanLine = (byte*)tempBitmapData.Scan0.ToPointer() + y * tempBitmapData.Stride;
                for (int x = 0; x < srcWidth; x++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        tempScanLine[3 * x + c] = inScanLine[3 * x + c];
                    }
                }
            }

            tempImg.UnlockBits(tempBitmapData);
            inImg.UnlockBits(inBitmapData);

            //Alle alten Seams verwerfen
            seams = new LinkedList<VerticalSeam>();

            //aktuelle Größe zurücksetzen
            this.currentWidth = srcWidth;
            this.currentHeight = srcHeight;

            //initial die Energiekarte füllen
            FillEnergyMap(EnergyMapCalculationType.Complete, null);

            //den Buffer für die dynamische Seam-Findung aktualisieren
            FillFlowMap();
        }


        enum EnergyMapCalculationType
        {
            Complete,
            PartialByShrinking,
            PartialByWidening,
        }

        /// <summary>
        /// Methode zum Füllen des Energiebildes
        /// </summary>
        /// <param name="seam">Seam der ergänzt/entfernt wurde (für inkrementelles Update wichtig)</param>
        /// <param name="calcType">Gibt die Art der Neuberechnung an</param>
        unsafe private void FillEnergyMap(EnergyMapCalculationType calcType, VerticalSeam seam)
        {
            //Bildzugriff
            BitmapData tempBitmapData = tempImg.LockBits(new Rectangle(new Point(0, 0), tempImg.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            int stride = tempBitmapData.Stride;

            //Energiebild füllen (Intensität des Gradienten mittels Sobel-Operator)
            //(über die drei Farbkanäle gemittelt)
            /*
             * Sobel berechnen geschieht über die (horizontalen/vertikalen) SobelKerne:
             *  | +1  0  -1 |           | +1  +2  +1 |
             *  | +2  0  -2 |    und    |  0   0   0 | 
             *  | +1  0  -1 |           | -1  -2  -1 |
             * 
             * und anschließender Längen-Berechnen des resultierenden Gradienten (dx, dy)
             *    sqrt(dx^2 + dy^2)
             */
            for (int y = 1; y < currentHeight - 1; y++)
            {
                byte* scanLine = (byte*)tempBitmapData.Scan0.ToPointer() + y * tempBitmapData.Stride;
                int energyMapOffset = y * srcWidth;

                //festlegen, in welchem Bereich aktualisiert werden muss
                int xStart = 1;
                int xEnd = currentWidth - 1;

                if (calcType != EnergyMapCalculationType.Complete)
                {
                    //der neuzuberechnende Bereich kann auf einen kleinen Raum um den Seam beschränkt werden
                    xStart = Math.Min(seam.xPositions[y - 1], Math.Min(seam.xPositions[y], seam.xPositions[y + 1])) - 1;
                    xEnd = Math.Max(seam.xPositions[y - 1], Math.Max(seam.xPositions[y], seam.xPositions[y + 1])) + 1;

                    xStart = Math.Max(1, xStart);
                    xEnd = Math.Min(currentWidth - 1, xEnd);
                }

                if (calcType == EnergyMapCalculationType.PartialByWidening)
                {
                    //hinter dem Bereich kann man die zuvor berechnete Werte übernehmen
                    for (int x = currentWidth - 2; x >= xEnd; x--)
                    {
                        energyMap[energyMapOffset + x] = energyMap[energyMapOffset + x - 1];
                    }
                }

                //im ermittelten Bereich die Werte komplett neu berechnen
                for (int x = xStart; x < xEnd; x++)
                {
                    int value = 0;
                    for (int c = 0; c < 3; c++)
                    {
                        int dx = (scanLine[3 * x - 3 - stride + c] + 2 * scanLine[3 * x - 3 + c] + scanLine[3 * x - 3 + stride + c])
                                - (scanLine[3 * x + 3 - stride + c] + 2 * scanLine[3 * x + 3 + c] + scanLine[3 * x + 3 + stride + c]);

                        int dy = (scanLine[3 * x - 3 - stride + c] + 2 * scanLine[3 * x - stride + c] + scanLine[3 * x + 3 - stride + c])
                                - (scanLine[3 * x - 3 + stride + c] + 2 * scanLine[3 * x + stride + c] + scanLine[3 * x + 3 + stride + c]);

                        value += (int)Math.Sqrt((double)(dx * dx + dy * dy));
                    }
                    energyMap[energyMapOffset + x] = value;
                }

                if (calcType == EnergyMapCalculationType.PartialByShrinking)
                {
                    //hinter dem Bereich kann man die zuvor berechnete Werte übernehmen
                    for (int x = xEnd; x < currentWidth - 1; x++)
                    {
                        energyMap[energyMapOffset + x] = energyMap[energyMapOffset + x + 1];
                    }
                }
            }

            //Umrandung extrapolieren
            //linker + rechter rand
            for (int y = 0; y < currentHeight; y++)
            {
                energyMap[y * srcWidth + 0] = energyMap[y * srcWidth + 1];
                energyMap[y * srcWidth + (currentWidth - 1)] = energyMap[y * srcWidth + (currentWidth - 2)];
            }
            //oberer + unterer rand
            for (int x = 0; x < currentWidth; x++)
            {
                energyMap[(0 * srcWidth) + x] = energyMap[(1 * srcWidth) + x];
                energyMap[((currentHeight - 1) * srcWidth) + x] = energyMap[((currentHeight - 2) * srcWidth) + x];
            }

            tempImg.UnlockBits(tempBitmapData);
        }


        /// <summary>
        /// Methode um den Buffer für die Seam-Findung zu füllen.
        /// Hierbei wird nach dem Optimalitätsprinzip von Bellman 
        /// Zeile für Zeile die Bewertungen der lokal besten Seams ermittelt
        /// </summary>
        unsafe private void FillFlowMap()
        {

            //erste Zeile übertragen
            for (int x = 0; x < currentWidth; x++)
            {
                flowMap[x] = energyMap[x];
            }

            fixed (int* unsafeFlowMap = flowMap)
            fixed (int* unsafeEnergyMap = energyMap)
            {
                //den Rest jeweils Zeile für Zeile berechnen
                //(Für jeden Pixel aus den drei angrenzenden Minimas der vorherigen Zeile)
                for (int y = 1; y < currentHeight; y++)
                {
                    int* currentFlowMapLine     = unsafeFlowMap     + (y * srcWidth);
                    int* previousFlowMapLine    = unsafeFlowMap     + ((y-1) * srcWidth);
                    int* currentEnergyMapLine   = unsafeEnergyMap   + (y * srcWidth);

                    //Sonderfall linker Rand (es gibt nur "unten" , "unten-rechts")
                    {
                        int x = 0;
                        currentFlowMapLine[x] = currentEnergyMapLine[x] 
                            + Math.Min(
                                previousFlowMapLine[x + 0], 
                                previousFlowMapLine[x + 1]);
                    }

                    //Normalfall ("unten-links" , "unten" , "unten-rechts")
                    for (int x = 1; x < currentWidth - 1; x++)
                    {
                        currentFlowMapLine[x] = currentEnergyMapLine[x] 
                            + Math.Min(Math.Min(
                                previousFlowMapLine[x - 1], 
                                previousFlowMapLine[x + 0]),
                                previousFlowMapLine[x + 1]);
                    }
                    //Sonderfall rechter Rand (es gibt nur "unten-links" , "unten")
                    {
                        int x = (currentWidth - 1);
                        currentFlowMapLine[x] = currentEnergyMapLine[x] 
                            + Math.Min(
                                previousFlowMapLine[x - 1], 
                                previousFlowMapLine[x + 0]);
                    }
                }

            }
        }


        /// <summary>
        /// Methode um aus dem Buffer für die Seam-Findung nun den besten Seam zu ermitteln
        /// </summary>
        /// <returns>Der beste Seam</returns>
        private VerticalSeam FindSeam()
        {
            VerticalSeam seam = new VerticalSeam(currentHeight);
            //Minimum in der obersten Zeile ermitteln (Ende des Seams)
            {
                int minVal = Int32.MaxValue;
                int topLineOffset = (currentHeight - 1) * srcWidth;
                for (int x = 0; x < currentWidth; x++)
                {
                    if (flowMap[topLineOffset + x] < minVal)
                    {
                        minVal = flowMap[topLineOffset + x];
                        seam.xPositions[currentHeight - 1] = x;
                    }
                }
            }

            //nun die restlichen Minima rekursiv durchlaufen und so den Seam rekonstruieren
            for (int y = currentHeight - 2; y >= 0; y--)
            {
                int flowMapOffset = y * srcWidth;
                int startX = seam.xPositions[y + 1];

                int minVal = flowMap[flowMapOffset + startX];
                seam.xPositions[y] = startX;
                if (startX > 0)
                {
                    if (flowMap[flowMapOffset + startX - 1] < minVal)
                    {
                        minVal = flowMap[flowMapOffset + startX - 1];
                        seam.xPositions[y] = startX - 1;
                    }
                }
                if (startX < currentWidth - 1)
                {
                    if (flowMap[flowMapOffset + startX + 1] < minVal)
                    {
                        seam.xPositions[y] = startX + 1;
                    }
                }
            }

            return seam;
        }

        /// <summary>
        /// Entfernt einen Seam aus einem Bild 
        /// (und übernimmt die Farbinformationen für eine spätere Restauration)
        /// </summary>
        /// <param name="seam">Seam, der entfernt werden soll</param>
        unsafe private void RemoveSeam(VerticalSeam seam)
        {
            BitmapData tempBitmapData = tempImg.LockBits(new Rectangle(new Point(0, 0), tempImg.Size), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);

            for (int y = 0; y < currentHeight; y++)
            {
                byte* scanLine = (byte*)tempBitmapData.Scan0.ToPointer() + y * tempBitmapData.Stride;

                //Farbe abspeichern
                seam.srcColors[y] = Color.FromArgb(
                    scanLine[3 * seam.xPositions[y] + 2],
                    scanLine[3 * seam.xPositions[y] + 1],
                    scanLine[3 * seam.xPositions[y] + 0]);

                //den Seam rausnehmen (alle Pixel rechts davon aufrücken lassen)
                for (int x = seam.xPositions[y]; x < currentWidth - 1; x++)
                {
                    scanLine[3 * x + 0] = scanLine[3 * (x + 1) + 0];
                    scanLine[3 * x + 1] = scanLine[3 * (x + 1) + 1];
                    scanLine[3 * x + 2] = scanLine[3 * (x + 1) + 2];
                }

            }

            tempImg.UnlockBits(tempBitmapData);

            this.currentWidth--;
        }

        /// <summary>
        /// Fügt einen entfernten Seam wieder in das Bild ein
        /// </summary>
        /// <param name="seam">Wieder einzufügender Seam</param>
        unsafe private void InsertSeam(VerticalSeam seam)
        {
            BitmapData tempBitmapData = tempImg.LockBits(new Rectangle(new Point(0, 0), tempImg.Size), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);

            for (int y = 0; y < currentHeight; y++)
            {
                byte* scanLine = (byte*)tempBitmapData.Scan0.ToPointer() + y * tempBitmapData.Stride;

                //Alle Pixel rechts vom Seam verschieben um Platz zu machen
                for (int x = currentWidth; x > seam.xPositions[y]; x--)
                {
                    scanLine[3 * x + 0] = scanLine[3 * (x - 1) + 0];
                    scanLine[3 * x + 1] = scanLine[3 * (x - 1) + 1];
                    scanLine[3 * x + 2] = scanLine[3 * (x - 1) + 2];
                }

                //Freigewordenen Pixel-Platz mit der Seam-Farbe füllen
                scanLine[3 * seam.xPositions[y] + 0] = seam.srcColors[y].B;
                scanLine[3 * seam.xPositions[y] + 1] = seam.srcColors[y].G;
                scanLine[3 * seam.xPositions[y] + 2] = seam.srcColors[y].R;
            }

            tempImg.UnlockBits(tempBitmapData);

            this.currentWidth++;
        }

        /// <summary>
        /// Methode um aus dem temporären Bildspeicher in-place ein Bitmap-Objekt zu erzeugen
        /// </summary>
        /// <returns>Das korrespondierende Bitmap</returns>
        unsafe private Bitmap CreateBitmap()
        {
            BitmapData tempBitmapData = tempImg.LockBits(new Rectangle(new Point(0, 0), tempImg.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            Bitmap result = new Bitmap(
                currentWidth, currentHeight,
                tempBitmapData.Stride,
                PixelFormat.Format24bppRgb,
                tempBitmapData.Scan0);

            tempImg.UnlockBits(tempBitmapData);
            return result;
        }
    }
}
