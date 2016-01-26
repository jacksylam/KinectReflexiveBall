﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using WindowsPreview.Kinect;
using System.ComponentModel;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Windows.UI.Xaml.Shapes;

namespace Kinect2Sample
{
    public enum DisplayFrameType
    {
        Infrared,
        Color,
        Depth,
        BodyMask,
        BodyJoints,
        Debug
    }

    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        private const DisplayFrameType DEFAULT_DISPLAYFRAMETYPE = DisplayFrameType.Infrared;

        /// <summary>
        /// The highest value that can be returned in the InfraredFrame.
        /// It is cast to a float for readability in the visualization code.
        /// </summary>
        private const float InfraredSourceValueMaximum = (float)ushort.MaxValue;

        /// <summary>
        /// Used to set the lower limit, post processing, of the
        /// infrared data that we will render.
        /// Increasing or decreasing this value sets a brightness 
        /// "wall" either closer or further away.
        /// </summary>
        private const float InfraredOutputValueMinimum = 0.01f;

        /// <summary>
        /// The upper limit, post processing, of the
        /// infrared data that will render.
        /// </summary>
        private const float InfraredOutputValueMaximum = 1.0f;

        /// <summary>
        /// The InfraredSceneValueAverage value specifies the average infrared 
        /// value of the scene. This value was selected by analyzing the average 
        /// pixel intensity for a given scene. 
        /// This could be calculated at runtime to handle different IR conditions
        /// of a scene (outside vs inside).
        /// </summary>
        private const float InfraredSceneValueAverage = 0.08f;

        /// <summary>
        /// The InfraredSceneStandardDeviations value specifies the number of 
        /// standard deviations to apply to InfraredSceneValueAverage. 
        /// This value was selected by analyzing data from a given scene.
        /// This could be calculated at runtime to handle different IR conditions
        /// of a scene (outside vs inside).
        /// </summary>
        private const float InfraredSceneStandardDeviations = 3.0f;

        // Size of the RGB pixel in the bitmap
        private const int BytesPerPixel = 4;

        private KinectSensor kinectSensor = null;
        private string statusText = null;
        private WriteableBitmap bitmap = null;
        private FrameDescription currentFrameDescription;
        private DisplayFrameType currentDisplayFrameType;
        private MultiSourceFrameReader multiSourceFrameReader = null;
        private CoordinateMapper coordinateMapper = null;
        private BodiesManager bodiesManager = null;

        //Infrared Frame 
        private ushort[] infraredFrameData = null;
        private byte[] infraredPixels = null;

        //Depth Frame
        private ushort[] depthFrameData = null;
        private byte[] depthPixels = null;

        //BodyMask Frames
        private DepthSpacePoint[] colorMappedToDepthPoints = null;

        //Body Joints are drawn here
        private Canvas drawingCanvas;

        //my stuff
        private Body[] myBodies;
        private int infraredWidth;
        private int infraredHeight;
        private int[] labels;
        private int[] binaryMat;

        public event PropertyChangedEventHandler PropertyChanged;
        public string StatusText
        {
            get { return this.statusText; }
            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }

        public FrameDescription CurrentFrameDescription
        {
            get { return this.currentFrameDescription; }
            set
            {
                if (this.currentFrameDescription != value)
                {
                    this.currentFrameDescription = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("CurrentFrameDescription"));
                    }
                }
            }
        }

        public MainPage()
        {
            // one sensor is currently supported
            this.kinectSensor = KinectSensor.GetDefault();

            SetupCurrentDisplay(DEFAULT_DISPLAYFRAMETYPE);

            this.coordinateMapper = this.kinectSensor.CoordinateMapper;

           // this.multiSourceFrameReader = this.kinectSensor.OpenMultiSourceFrameReader(FrameSourceTypes.Infrared | FrameSourceTypes.Color | FrameSourceTypes.Depth | FrameSourceTypes.BodyIndex | FrameSourceTypes.Body);


            this.multiSourceFrameReader = this.kinectSensor.OpenMultiSourceFrameReader(FrameSourceTypes.Infrared |  FrameSourceTypes.Depth | FrameSourceTypes.BodyIndex | FrameSourceTypes.Body);

            this.multiSourceFrameReader.MultiSourceFrameArrived += this.Reader_MultiSourceFrameArrived;

            // set IsAvailableChanged event notifier
            this.kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;

            // use the window object as the view model in this simple example
            this.DataContext = this;

            // open the sensor
            this.kinectSensor.Open();

            this.InitializeComponent();
        }

        private void SetupCurrentDisplay(DisplayFrameType newDisplayFrameType)
        {
            currentDisplayFrameType = newDisplayFrameType;
            // Frames used by more than one type are declared outside the switch
            FrameDescription colorFrameDescription = null;
            // reset the display methods
            if (this.BodyJointsGrid != null)
            {
                this.BodyJointsGrid.Visibility = Visibility.Collapsed;
            }
            if (this.FrameDisplayImage != null)
            {
                this.FrameDisplayImage.Source = null;
            }
            switch (currentDisplayFrameType)
            {
                case DisplayFrameType.Infrared:
                    FrameDescription infraredFrameDescription = this.kinectSensor.InfraredFrameSource.FrameDescription;
                    this.CurrentFrameDescription = infraredFrameDescription;
                    // allocate space to put the pixels being received and converted
                    this.infraredFrameData = new ushort[infraredFrameDescription.Width * infraredFrameDescription.Height];
                    this.infraredPixels = new byte[infraredFrameDescription.Width * infraredFrameDescription.Height * BytesPerPixel];
                    this.bitmap = new WriteableBitmap(infraredFrameDescription.Width, infraredFrameDescription.Height);
                    break;

                case DisplayFrameType.Debug:
                    infraredFrameDescription = this.kinectSensor.InfraredFrameSource.FrameDescription;
                    this.CurrentFrameDescription = infraredFrameDescription;
                    // allocate space to put the pixels being received and converted
                    this.infraredFrameData = new ushort[infraredFrameDescription.Width * infraredFrameDescription.Height];
                    this.infraredPixels = new byte[infraredFrameDescription.Width * infraredFrameDescription.Height * BytesPerPixel];
                    this.bitmap = new WriteableBitmap(infraredFrameDescription.Width, infraredFrameDescription.Height);
                    break;

                case DisplayFrameType.Color:
                    colorFrameDescription = this.kinectSensor.ColorFrameSource.FrameDescription;
                    this.CurrentFrameDescription = colorFrameDescription;
                    // create the bitmap to display
                    this.bitmap = new WriteableBitmap(colorFrameDescription.Width, colorFrameDescription.Height);
                    break;

                case DisplayFrameType.Depth:
                    FrameDescription depthFrameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;
                    this.CurrentFrameDescription = depthFrameDescription;
                    // allocate space to put the pixels being received and converted
                    this.depthFrameData = new ushort[depthFrameDescription.Width * depthFrameDescription.Height];
                    this.depthPixels = new byte[depthFrameDescription.Width * depthFrameDescription.Height * BytesPerPixel];
                    this.bitmap = new WriteableBitmap(depthFrameDescription.Width, depthFrameDescription.Height);
                    break;

                case DisplayFrameType.BodyMask:
                    colorFrameDescription = this.kinectSensor.ColorFrameSource.FrameDescription;
                    this.CurrentFrameDescription = colorFrameDescription;
                    // allocate space to put the pixels being received and converted
                    this.colorMappedToDepthPoints = new DepthSpacePoint[colorFrameDescription.Width * colorFrameDescription.Height];
                    this.bitmap = new WriteableBitmap(colorFrameDescription.Width, colorFrameDescription.Height);
                    break;

                case DisplayFrameType.BodyJoints:
                    // instantiate a new Canvas
                    this.drawingCanvas = new Canvas();
                    // set the clip rectangle to prevent rendering outside the canvas
                    this.drawingCanvas.Clip = new RectangleGeometry();
                    this.drawingCanvas.Clip.Rect = new Rect(0.0, 0.0, this.BodyJointsGrid.Width, this.BodyJointsGrid.Height);
                    this.drawingCanvas.Width = this.BodyJointsGrid.Width;
                    this.drawingCanvas.Height = this.BodyJointsGrid.Height;
                    // reset the body joints grid
                    this.BodyJointsGrid.Visibility = Visibility.Visible;
                    this.BodyJointsGrid.Children.Clear();
                    // add canvas to DisplayGrid
                    this.BodyJointsGrid.Children.Add(this.drawingCanvas);
                    bodiesManager = new BodiesManager(this.coordinateMapper, this.drawingCanvas, this.kinectSensor.BodyFrameSource.BodyCount);
                    break;
                default:
                    break;
            }
        }

        private void Sensor_IsAvailableChanged(KinectSensor sender, IsAvailableChangedEventArgs args)
        {
            this.StatusText = this.kinectSensor.IsAvailable ? "Running" : "Not Available";
        }

        private void Reader_MultiSourceFrameArrived(MultiSourceFrameReader sender, MultiSourceFrameArrivedEventArgs e)
        {
            
            MultiSourceFrame multiSourceFrame = e.FrameReference.AcquireFrame();

            // If the Frame has expired by the time we process this event, return.
            if (multiSourceFrame == null)
            {
                return;
            }
            DepthFrame depthFrame = null;
            ColorFrame colorFrame = null;
            InfraredFrame infraredFrame = null;
            BodyFrame bodyFrame = null;
            BodyIndexFrame bodyIndexFrame = null;
            IBuffer depthFrameData = null;
            IBuffer bodyIndexFrameData = null;
            // Com interface for unsafe byte manipulation
            IBufferByteAccess bodyIndexByteAccess = null;

            switch (currentDisplayFrameType)
            {
                case DisplayFrameType.Infrared:
                    using (infraredFrame = multiSourceFrame.InfraredFrameReference.AcquireFrame())
                    using(bodyFrame = multiSourceFrame.BodyFrameReference.AcquireFrame())
                    {
                        ShowInfraredFrame(infraredFrame, bodyFrame);
                    }
                    break;
                case DisplayFrameType.Debug:
                    using (infraredFrame = multiSourceFrame.InfraredFrameReference.AcquireFrame())
                    using(bodyFrame = multiSourceFrame.BodyFrameReference.AcquireFrame())
                    {
                        ShowDebugFrame(infraredFrame, bodyFrame);
                    }
                    break;

                case DisplayFrameType.Color:
                    using (colorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame())
                    {
                        ShowColorFrame(colorFrame);
                    }
                    break;
                case DisplayFrameType.Depth:
                    using (depthFrame = multiSourceFrame.DepthFrameReference.AcquireFrame())
                    {
                        ShowDepthFrame(depthFrame);
                    }
                    break;
                case DisplayFrameType.BodyMask:
                    // Put in a try catch to utilise finally() and clean up frames
                    try
                    {
                        depthFrame = multiSourceFrame.DepthFrameReference.AcquireFrame();
                        bodyIndexFrame = multiSourceFrame.BodyIndexFrameReference.AcquireFrame();
                        colorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame();
                        if ((depthFrame == null) || (colorFrame == null) || (bodyIndexFrame == null))
                        {
                            return;
                        }

                        // Access the depth frame data directly via LockImageBuffer to avoid making a copy
                        depthFrameData = depthFrame.LockImageBuffer();
                        this.coordinateMapper.MapColorFrameToDepthSpaceUsingIBuffer(depthFrameData, this.colorMappedToDepthPoints);
                        // Process Color
                        colorFrame.CopyConvertedFrameDataToBuffer(this.bitmap.PixelBuffer, ColorImageFormat.Bgra);
                        // Access the body index frame data directly via LockImageBuffer to avoid making a copy
                        bodyIndexFrameData = bodyIndexFrame.LockImageBuffer();
                        ShowMappedBodyFrame(depthFrame.FrameDescription.Width, depthFrame.FrameDescription.Height, bodyIndexFrameData, bodyIndexByteAccess);

                    }
                    finally
                    {
                        // ... disposing of depth, color and bodyIndex frames
                        if (depthFrame != null)
                        {
                            depthFrame.Dispose();
                        }
                        if (colorFrame != null)
                        {
                            colorFrame.Dispose();
                        }
                        if (bodyIndexFrame != null)
                        {
                            bodyIndexFrame.Dispose();
                        }

                        if (depthFrameData != null)
                        {
                            // We must force a release of the IBuffer in order to ensure that we have dropped all references to it.
                            System.Runtime.InteropServices.Marshal.ReleaseComObject(depthFrameData);
                        }
                        if (bodyIndexFrameData != null)
                        {
                            System.Runtime.InteropServices.Marshal.ReleaseComObject(bodyIndexFrameData);
                        }
                        if (bodyIndexByteAccess != null)
                        {
                            System.Runtime.InteropServices.Marshal.ReleaseComObject(bodyIndexByteAccess);
                        }

                    }
                    break;
                case DisplayFrameType.BodyJoints:
                    using (bodyFrame = multiSourceFrame.BodyFrameReference.AcquireFrame())
                    {
                        ShowBodyJoints(bodyFrame);
                    }
                    break;
                default:
                    break;
            }
        }

        private void ShowBodyJoints(BodyFrame bodyFrame)
        {
            Body[] bodies = new Body[this.kinectSensor.BodyFrameSource.BodyCount];
            bool dataReceived = false;
            if (bodyFrame != null)
            {
                bodyFrame.GetAndRefreshBodyData(bodies);
                dataReceived = true;
            }

            if (dataReceived)
            {
                this.bodiesManager.UpdateBodiesAndEdges(bodies);
            }
        }

        unsafe private void ShowMappedBodyFrame(int depthWidth, int depthHeight, IBuffer bodyIndexFrameData, IBufferByteAccess bodyIndexByteAccess)
        {
            bodyIndexByteAccess = (IBufferByteAccess)bodyIndexFrameData;
            byte* bodyIndexBytes = null;
            bodyIndexByteAccess.Buffer(out bodyIndexBytes);

            fixed (DepthSpacePoint* colorMappedToDepthPointsPointer = this.colorMappedToDepthPoints)
            {
                IBufferByteAccess bitmapBackBufferByteAccess = (IBufferByteAccess)this.bitmap.PixelBuffer;

                byte* bitmapBackBufferBytes = null;
                bitmapBackBufferByteAccess.Buffer(out bitmapBackBufferBytes);

                // Treat the color data as 4-byte pixels
                uint* bitmapPixelsPointer = (uint*)bitmapBackBufferBytes;

                // Loop over each row and column of the color image
                // Zero out any pixels that don't correspond to a body index
                int colorMappedLength = this.colorMappedToDepthPoints.Length;
                for (int colorIndex = 0; colorIndex < colorMappedLength; ++colorIndex)
                {
                    float colorMappedToDepthX = colorMappedToDepthPointsPointer[colorIndex].X;
                    float colorMappedToDepthY = colorMappedToDepthPointsPointer[colorIndex].Y;

                    // The sentinel value is -inf, -inf, meaning that no depth pixel corresponds to this color pixel.
                    if (!float.IsNegativeInfinity(colorMappedToDepthX) &&
                        !float.IsNegativeInfinity(colorMappedToDepthY))
                    {
                        // Make sure the depth pixel maps to a valid point in color space
                        int depthX = (int)(colorMappedToDepthX + 0.5f);
                        int depthY = (int)(colorMappedToDepthY + 0.5f);

                        // If the point is not valid, there is no body index there.
                        if ((depthX >= 0) && (depthX < depthWidth) && (depthY >= 0) && (depthY < depthHeight))
                        {
                            int depthIndex = (depthY * depthWidth) + depthX;

                            // If we are tracking a body for the current pixel, do not zero out the pixel
                            if (bodyIndexBytes[depthIndex] != 0xff)
                            {
                                // this bodyIndexByte is good and is a body, loop again.
                                continue;
                            }
                        }
                    }
                    // this pixel does not correspond to a body so make it black and transparent
                    bitmapPixelsPointer[colorIndex] = 0;
                }
            }

            this.bitmap.Invalidate();
            FrameDisplayImage.Source = this.bitmap;

        }

        private void ShowDepthFrame(DepthFrame depthFrame)
        {
            bool depthFrameProcessed = false;
            ushort minDepth = 0;
            ushort maxDepth = 0;

            if (depthFrame != null)
            {
                FrameDescription depthFrameDescription = depthFrame.FrameDescription;

                // verify data and write the new infrared frame data to the display bitmap
                if (((depthFrameDescription.Width * depthFrameDescription.Height)
                    == this.infraredFrameData.Length) &&
                    (depthFrameDescription.Width == this.bitmap.PixelWidth) &&
                    (depthFrameDescription.Height == this.bitmap.PixelHeight))
                {
                    // Copy the pixel data from the image to a temporary array
                    depthFrame.CopyFrameDataToArray(this.depthFrameData);

                    minDepth = depthFrame.DepthMinReliableDistance;
                    maxDepth = depthFrame.DepthMaxReliableDistance;
                    //maxDepth = 8000;

                    depthFrameProcessed = true;
                }
            }

            // we got a frame, convert and render
            if (depthFrameProcessed)
            {
                ConvertDepthDataToPixels(minDepth, maxDepth);
                RenderPixelArray(this.depthPixels);
            }
        }

        private void ConvertDepthDataToPixels(ushort minDepth, ushort maxDepth)
        {
            int colorPixelIndex = 0;
            // Shape the depth to the range of a byte
            int mapDepthToByte = maxDepth / 256;

            for (int i = 0; i < this.depthFrameData.Length; ++i)
            {
                // Get the depth for this pixel
                ushort depth = this.depthFrameData[i];

                // To convert to a byte, we're mapping the depth value to the byte range.
                // Values outside the reliable depth range are mapped to 0 (black).
                byte intensity = (byte)(depth >= minDepth &&
                    depth <= maxDepth ? (depth / mapDepthToByte) : 0);

                this.depthPixels[colorPixelIndex++] = intensity; //Blue
                this.depthPixels[colorPixelIndex++] = intensity; //Green
                this.depthPixels[colorPixelIndex++] = intensity; //Red
                this.depthPixels[colorPixelIndex++] = 255; //Alpha
            }
        }

        private void ShowColorFrame(ColorFrame colorFrame)
        {
            bool colorFrameProcessed = false;

            if (colorFrame != null)
            {
                FrameDescription colorFrameDescription = colorFrame.FrameDescription;

                // verify data and write the new color frame data to the Writeable bitmap
                if ((colorFrameDescription.Width == this.bitmap.PixelWidth) && (colorFrameDescription.Height == this.bitmap.PixelHeight))
                {
                    if (colorFrame.RawColorImageFormat == ColorImageFormat.Bgra)
                    {
                        colorFrame.CopyRawFrameDataToBuffer(this.bitmap.PixelBuffer);
                    }
                    else
                    {
                        colorFrame.CopyConvertedFrameDataToBuffer(this.bitmap.PixelBuffer, ColorImageFormat.Bgra);
                    }

                    colorFrameProcessed = true;
                }
            }

            if (colorFrameProcessed)
            {
                this.bitmap.Invalidate();
                FrameDisplayImage.Source = this.bitmap;
            }
        }

        private void ShowInfraredFrame(InfraredFrame infraredFrame, BodyFrame bodyFrame)
        {
            bool infraredFrameProcessed = false;

            if (infraredFrame != null)
            {
                FrameDescription infraredFrameDescription = infraredFrame.FrameDescription;

                // verify data and write the new infrared frame data to the display bitmap
                if (((infraredFrameDescription.Width * infraredFrameDescription.Height)
                    == this.infraredFrameData.Length) &&
                    (infraredFrameDescription.Width == this.bitmap.PixelWidth) &&
                    (infraredFrameDescription.Height == this.bitmap.PixelHeight))
                {

                    //Debug.WriteLine("Width is " + infraredFrameDescription.Width);
                    //Debug.WriteLine("Height is " + infraredFrameDescription.Height);

                    infraredWidth = infraredFrameDescription.Width;
                    infraredHeight = infraredFrameDescription.Height;

                    // Copy the pixel data from the image to a temporary array
                    infraredFrame.CopyFrameDataToArray(this.infraredFrameData);

                    infraredFrameProcessed = true;
                }
            }

         
            if (bodyFrame != null && bodyFrame.BodyCount > 0)
            {
                myBodies = new Body[this.kinectSensor.BodyFrameSource.BodyCount];
                bodyFrame.GetAndRefreshBodyData(myBodies);
            }

            // we got a frame, convert and render
            if (infraredFrameProcessed)
            {
                this.ConvertInfraredDataToPixels(myBodies);
                this.RenderPixelArray(this.infraredPixels);
            }
        }

        private void ConvertInfraredDataToPixels(Body[] myBodies)
        {
            // Convert the infrared to RGB
            int colorPixelIndex = 0;
            for (int i = 0; i < this.infraredFrameData.Length; ++i)
            {
                // normalize the incoming infrared data (ushort) to a float ranging from 
                // [InfraredOutputValueMinimum, InfraredOutputValueMaximum] by
                // 1. dividing the incoming value by the source maximum value
                float intensityRatio = (float)this.infraredFrameData[i] / InfraredSourceValueMaximum;

                // 2. dividing by the (average scene value * standard deviations)
                intensityRatio /= InfraredSceneValueAverage * InfraredSceneStandardDeviations;

                // 3. limiting the value to InfraredOutputValueMaximum
                intensityRatio = Math.Min(InfraredOutputValueMaximum, intensityRatio);

                // 4. limiting the lower value InfraredOutputValueMinimum
                intensityRatio = Math.Max(InfraredOutputValueMinimum, intensityRatio);

                // 5. converting the normalized value to a byte and using the result
                // as the RGB components required by the image
                byte intensity = (byte)(intensityRatio * 255.0f);
                if (intensity > 254)
                {
                    bool isRetroReflexiveBall = false;
                    foreach(Body body in myBodies){
                        //get the left and right hand joints
                        Joint rightHand = body.Joints[JointType.HandRight];
                        Joint leftHand = body.Joints[JointType.HandLeft];

                        Joint spineMid = body.Joints[JointType.SpineMid];
                        Joint spineBase = body.Joints[JointType.SpineBase];

                        //Convert Camera space (for body) to Depth space (for Innfrared)
                        DepthSpacePoint rightPoint = this.kinectSensor.CoordinateMapper.MapCameraPointToDepthSpace(rightHand.Position);
                        DepthSpacePoint leftPoint = this.kinectSensor.CoordinateMapper.MapCameraPointToDepthSpace(leftHand.Position);
                        int rightHandX = (int) rightPoint.X;
                        int rightHandY = (int) rightPoint.Y;
                        int leftHandX = (int) leftPoint.X;
                        int leftHandY = (int)leftPoint.Y;

                        DepthSpacePoint spineMidPoint = this.kinectSensor.CoordinateMapper.MapCameraPointToDepthSpace(spineMid.Position);
                        int spineMidX = (int)spineMidPoint.X;
                        int spineMidY = (int) spineMidPoint.Y;

                        DepthSpacePoint spineBasePoint = this.kinectSensor.CoordinateMapper.MapCameraPointToDepthSpace(spineBase.Position);
                        int spineBaseX = (int)spineBasePoint.X;
                        int spineBaseY = (int)spineBasePoint.Y;


                        int spineAverageX = (spineMidX + spineBaseX) / 2;
                        int spineAverageY = (spineMidY + spineBaseY) / 2;

                        //Find x and y from 1D array
                        int indexX = i % infraredWidth;
                        int indexY = i / infraredWidth;

                        //range to check
                        if (((indexX < rightHandX + 20 && indexX > rightHandX - 20) &&
                            (indexY < rightHandY + 20 && indexY > rightHandY - 20)) 
                            ||
                            ((indexX < leftHandX + 20 && indexX > leftHandX - 20) &&
                            (indexY < leftHandY + 20 && indexY > leftHandY - 20)))
                        {
                            if (indexX < spineAverageX && indexY < spineAverageY)
                            {
                                this.infraredPixels[colorPixelIndex++] = 0; //Blue
                                this.infraredPixels[colorPixelIndex++] = 0; //Green
                                this.infraredPixels[colorPixelIndex++] = intensity; //Red
                                this.infraredPixels[colorPixelIndex++] = 255;       //Alpha
                            }
                            else if (indexX > spineAverageX && indexY < spineAverageY)
                            {
                                this.infraredPixels[colorPixelIndex++] = intensity; //Blue
                                this.infraredPixels[colorPixelIndex++] = 0; //Green
                                this.infraredPixels[colorPixelIndex++] = 0; //Red
                                this.infraredPixels[colorPixelIndex++] = 255;       //Alpha
                            }
                            else if (indexX < spineAverageX && indexY > spineAverageY)
                            {
                                this.infraredPixels[colorPixelIndex++] = 0; //Blue
                                this.infraredPixels[colorPixelIndex++] = intensity; //Green
                                this.infraredPixels[colorPixelIndex++] = 0; //Red
                                this.infraredPixels[colorPixelIndex++] = 255;       //Alpha
                            }
                            else
                            {
                                this.infraredPixels[colorPixelIndex++] = intensity; //Blue
                                this.infraredPixels[colorPixelIndex++] = intensity; //Green
                                this.infraredPixels[colorPixelIndex++] = 0; //Red
                                this.infraredPixels[colorPixelIndex++] = 255;       //Alpha
                            }
                           
                            isRetroReflexiveBall = true;
                            break;
                        }


                    }

                    //The retroreflexive is not near a hand. Probably not something we have to track.
                    if (isRetroReflexiveBall == false)
                    {
                        this.infraredPixels[colorPixelIndex++] = intensity; //Blue
                        this.infraredPixels[colorPixelIndex++] = intensity; //Green
                        this.infraredPixels[colorPixelIndex++] = intensity; //Red
                        this.infraredPixels[colorPixelIndex++] = 255;       //Alpha  
                    }
                    
                }
                else
                {
                    this.infraredPixels[colorPixelIndex++] = intensity; //Blue
                    this.infraredPixels[colorPixelIndex++] = intensity; //Green
                    this.infraredPixels[colorPixelIndex++] = intensity; //Red
                    this.infraredPixels[colorPixelIndex++] = 255;       //Alpha  
                }      
            }
        }

        private void RenderPixelArray(byte[] pixels)
        {
            pixels.CopyTo(this.bitmap.PixelBuffer);
            this.bitmap.Invalidate();
            this.FrameDisplayImage.Source = this.bitmap;
        }

        private void makeBinaryMat() {
            binaryMat = new int[this.infraredFrameData.Length];

            for (int i = 0; i < this.infraredFrameData.Length; i++) {
                // normalize the incoming infrared data (ushort) to a float ranging from 
                // [InfraredOutputValueMinimum, InfraredOutputValueMaximum] by
                // 1. dividing the incoming value by the source maximum value
                float intensityRatio = (float)this.infraredFrameData[i] / InfraredSourceValueMaximum;

                // 2. dividing by the (average scene value * standard deviations)
                intensityRatio /= InfraredSceneValueAverage * InfraredSceneStandardDeviations;

                // 3. limiting the value to InfraredOutputValueMaximum
                intensityRatio = Math.Min(InfraredOutputValueMaximum, intensityRatio);

                // 4. limiting the lower value InfraredOutputValueMinimum
                intensityRatio = Math.Max(InfraredOutputValueMinimum, intensityRatio);

                // 5. converting the normalized value to a byte and using the result
                // as the RGB components required by the image
                byte intensity = (byte)(intensityRatio * 255.0f);

                if (intensity > 254) {
                    binaryMat[i] = 1;
                }
                else {
                    binaryMat[i] = 0;
                }
            }
            
        }

        private int[] dialate(int[] binaryMat) {
            int[] ret = new int[this.binaryMat.Length];

            for (int i = 0; i < this.binaryMat.Length; i++) {
                if (this.binaryMat[i] == 1) {
                    ret[i] = 1;
                    if (i + 1 % infraredWidth != 0) {
                        ret[i + 1] = 1;
                    }
                    if (i - 1 % infraredWidth != infraredWidth - 1) {
                        ret[i - 1] = 1;
                    }
                    if (i - infraredWidth < 0) {
                        ret[i - infraredWidth] = 1;
                    }
                    if (i + infraredWidth < this.binaryMat.Length) {
                        ret[i + infraredWidth] = 1;
                    }
                }
                else {
                    ret[i] = 0;
                }
            }
            return ret;
        }

        private int[] erode(int[] binaryMat) {
            int[] ret = new int[this.binaryMat.Length];

            for (int i = 0; i < this.binaryMat.Length; i++) {
                if (i%infraredWidth != 0 && i > infraredWidth && i < binaryMat.Length && i%infraredWidth != infraredWidth - 1 && 
                    binaryMat[i] == 1 && binaryMat[i+1] == 1 && binaryMat[i-1] == 1 && binaryMat[i+infraredWidth] == 1 && binaryMat[i-infraredWidth] == 1) {
                    ret[i] = 1;
                }
                else {
                    ret[i] = 0;
                }
            }
            return ret;
        }

        private void getConnectedComponents()
        {
            LabelArray equivalentLabels = new LabelArray();
            labels = new int[this.binaryMat.Length];
            int labelCount = 1;

            for (int i = 0; i < this.binaryMat.Length; ++i)
            {
                if (binaryMat[i] == 1)
                {
                    if (i > infraredWidth && i%infraredWidth != 0 && labels[i - infraredWidth] != 0 && labels[i - 1] == 0) {
                        labels[i] = labels[i - infraredWidth];
                    }
                    else if (i > infraredWidth && i % infraredWidth != 0 && labels[i - infraredWidth] == 0 && labels[i - 1] != 0) {
                        labels[i] = labels[i - 1];
                    }
                    else if (i > infraredWidth && i % infraredWidth != 0 && labels[i - infraredWidth] == 0 && labels[i - 1] == 0) {
                        labels[i] = labelCount;
                        labelCount++;
                    }
                    else if (i > infraredWidth && i % infraredWidth != 0 && labels[i - infraredWidth] != 0 && labels[i - 1] != 0) {
                        labels[i] = labels[i - 1];
                        equivalentLabels.Add(labels[i - 1], labels[i - infraredWidth]);
                    }
                    else {
                        labels[i] = 0;
                    }
                }
                else {
                    labels[i] = 0;
                }
            }

            //perform union
            for (int i = 0; i < labels.Length; i++) {
                if (labels[i] != 0){
                    int tempLabel = equivalentLabels.getEquivalentLabel(labels[i]);
                    if (tempLabel != 0) {
                        labels[i] = tempLabel;
                    }
                }
            }

            Debug.WriteLine("Number of labels = " + equivalentLabels.Size());
        }

        private void displayConnectedComponents(){
            this.makeBinaryMat();
           binaryMat =  this.erode(binaryMat);
           binaryMat = this.dialate(binaryMat);
            this.getConnectedComponents();
            byte intensity = 255;

            int colorPixelIndex = 0;
            for (int i = 0; i < labels.Length; i++) {
                if (labels[i] != 0) {
                    this.infraredPixels[colorPixelIndex++] = intensity; //Blue
                    this.infraredPixels[colorPixelIndex++] = intensity; //Green
                    this.infraredPixels[colorPixelIndex++] = intensity; //Red
                    this.infraredPixels[colorPixelIndex++] = 255;       //Alpha  
                }
                else {
                    this.infraredPixels[colorPixelIndex++] = 0; //Blue
                    this.infraredPixels[colorPixelIndex++] = 0; //Green
                    this.infraredPixels[colorPixelIndex++] = 0; //Red
                    this.infraredPixels[colorPixelIndex++] = 255;       //Alpha  
                }
            }
        }

        private void ShowDebugFrame(InfraredFrame infraredFrame, BodyFrame bodyFrame) {
            bool infraredFrameProcessed = false;

            if (infraredFrame != null) {
                FrameDescription infraredFrameDescription = infraredFrame.FrameDescription;

                // verify data and write the new infrared frame data to the display bitmap
                if (((infraredFrameDescription.Width * infraredFrameDescription.Height)
                    == this.infraredFrameData.Length) &&
                    (infraredFrameDescription.Width == this.bitmap.PixelWidth) &&
                    (infraredFrameDescription.Height == this.bitmap.PixelHeight)) {

                    //Debug.WriteLine("Width is " + infraredFrameDescription.Width);
                    //Debug.WriteLine("Height is " + infraredFrameDescription.Height);

                    infraredWidth = infraredFrameDescription.Width;
                    infraredHeight = infraredFrameDescription.Height;

                    // Copy the pixel data from the image to a temporary array
                    infraredFrame.CopyFrameDataToArray(this.infraredFrameData);

                    infraredFrameProcessed = true;
                }
            }


            if (bodyFrame != null && bodyFrame.BodyCount > 0) {
                myBodies = new Body[this.kinectSensor.BodyFrameSource.BodyCount];
                bodyFrame.GetAndRefreshBodyData(myBodies);
            }

            // we got a frame, convert and render
            if (infraredFrameProcessed) {
                this.displayConnectedComponents();
                this.RenderPixelArray(this.infraredPixels);
            }
        }

        //128 quadrant
        private void dualTree() {
            int[] histogram = new int[128];

            for (int i = 0; i < binaryMat.Length; i++) {
                if (binaryMat[i] == 1) {
                    
                }
            }
        }


        private void InfraredButton_Click(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.Infrared);
        }

        private void DebugButton_Click(object sender, RoutedEventArgs e) {
            SetupCurrentDisplay(DisplayFrameType.Debug);
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.Color);
        }

        private void DepthButton_Click(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.Depth);
        }

        private void BodyMaskButton_Click(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.BodyMask);
        }

        private void BodyJointsButton_Click(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.BodyJoints);
        }

        [Guid("905a0fef-bc53-11df-8c49-001e4fc686da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IBufferByteAccess
        {
            unsafe void Buffer(out byte* pByte);
        }



    }
}
