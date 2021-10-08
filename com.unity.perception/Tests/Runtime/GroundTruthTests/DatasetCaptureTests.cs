using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Simulation;
using UnityEngine;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.Perception.GroundTruth.Consumers;
using UnityEngine.Perception.GroundTruth.DataModel;
using UnityEngine.TestTools;
// ReSharper disable InconsistentNaming
// ReSharper disable NotAccessedField.Local

namespace GroundTruthTests
{
    public static class AssertUtils
    {
        public static void AreEqual(Vector2 first, Vector2 second)
        {
            Assert.AreEqual(first.x, second.x);
            Assert.AreEqual(first.y, second.y);
        }

        public static void AreEqual(Vector3 first, Vector3 second)
        {
            Assert.AreEqual(first.x, second.x);
            Assert.AreEqual(first.y, second.y);
            Assert.AreEqual(first.z, second.z);
        }

        public static void AreEqual(float3x3 first, float3x3 second)
        {
            for (var i = 0; i < 9; i++)
            {
                Assert.AreEqual(first[i], second[i]);
            }
        }

        public static void AreEqual(RgbSensor first, RgbSensor second)
        {
            Assert.NotNull(first);
            Assert.NotNull(second);

            Assert.AreEqual(first.Id, second.Id);
            Assert.AreEqual(first.sensorType, second.sensorType);
            Assert.AreEqual(first.description, second.description);
            AreEqual(first.position, second.position);
            AreEqual(first.rotation, second.rotation);
            AreEqual(first.velocity, second.velocity);
            AreEqual(first.acceleration, second.acceleration);
            Assert.AreEqual(first.intrinsics, second.intrinsics);
            Assert.AreEqual(first.imageFormat, second.imageFormat);
            AreEqual(first.dimension, second.dimension);
            Assert.Null(first.buffer);
            Assert.Null(second.buffer);
        }
    }

    [TestFixture]
    public class DatasetCaptureTests
    {
        static SensorHandle RegisterSensor(string id, string modality, string sensorDescription, int firstCaptureFrame, CaptureTriggerMode captureTriggerMode, float simDeltaTime, int framesBetween, bool affectTiming = false)
        {
            var sensorDefinition = new SensorDefinition(id, modality, sensorDescription)
            {
                firstCaptureFrame = firstCaptureFrame,
                captureTriggerMode = captureTriggerMode,
                simulationDeltaTime = simDeltaTime,
                framesBetweenCaptures = framesBetween,
                manualSensorsAffectTiming = affectTiming
            };
            return DatasetCapture.Instance.RegisterSensor(sensorDefinition);
        }

        [UnityTest]
        public IEnumerator RegisterSensor_ReportsProperJson()
        {
            const string id = "camera";
            const string modality = "camera";
            const string def = "Cam (FL2-14S3M-C)";
            const int firstFrame = 1;
            const CaptureTriggerMode mode = CaptureTriggerMode.Scheduled;
            const int delta = 1;
            const int framesBetween = 0;

            var collector = new CollectEndpoint();

            DatasetCapture.SetEndpoint(collector);

            var sensorHandle = RegisterSensor(id, modality, def, firstFrame, mode, delta, framesBetween);
            Assert.IsTrue(sensorHandle.IsValid);

            yield return null;
            DatasetCapture.Instance.ResetSimulation();
            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;

            Assert.IsFalse(sensorHandle.IsValid);

            // Check metadata
            var meta = collector.currentRun.metadata as CompletionMetadata;
            Assert.NotNull(meta);
            Assert.AreEqual(meta.perceptionVersion, DatasetCapture.PerceptionVersion);
            Assert.AreEqual(meta.unityVersion, Application.unityVersion);

            // Check sensor data
            Assert.AreEqual(collector.sensors.Count, 1);
            var sensor = collector.sensors.First();
            Assert.NotNull(sensor);
            Assert.AreEqual(sensor.id, id);
            Assert.AreEqual(sensor.modality, modality);
            Assert.AreEqual(sensor.definition, def);
            Assert.AreEqual(sensor.firstCaptureFrame, firstFrame);
            Assert.AreEqual(sensor.captureTriggerMode, mode);
            Assert.AreEqual(sensor.simulationDeltaTime, delta);
            Assert.AreEqual(sensor.framesBetweenCaptures, framesBetween);
        }

        RgbSensor CreateMocRgbCapture()
        {
            var position = new float3(.2f, 1.1f, .3f);
            var rotation = new Quaternion(.3f, .2f, .1f, .5f);
            var velocity = new Vector3(.1f, .2f, .3f);
            var intrinsics = new float3x3(.1f, .2f, .3f, 1f, 2f, 3f, 10f, 20f, 30f);

            return new RgbSensor
            {
                position = position,
                rotation = rotation.eulerAngles,
                velocity = velocity,
                intrinsics = intrinsics
            };
        }

        [UnityTest]
        public IEnumerator ReportCaptureAsync_TimesOut()
        {
            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);
            SimulationState.TimeOutFrameCount = 5;

            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);
            var f = sensorHandle.ReportSensorAsync();

            for (var i = 0; i <= SimulationState.TimeOutFrameCount; i++) yield return null;

            DatasetCapture.Instance.ResetSimulation();
            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;

            LogAssert.Expect(LogType.Error, new Regex("A frame has timed out and is being removed.*"));
            SimulationState.TimeOutFrameCount = 6000;
        }

        [UnityTest]
        public IEnumerator ReportCaptureAsync_DoesNotTimeOut()
        {
            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);
            SimulationState.TimeOutFrameCount = 5;

            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);
            var f = sensorHandle.ReportSensorAsync();

            yield return null;
            yield return null;

            f.Report(CreateMocRgbCapture());

            DatasetCapture.Instance.ResetSimulation();
            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;

            Assert.AreEqual(1, collector.currentRun.frames.Count);
            SimulationState.TimeOutFrameCount = 6000;
        }

        [UnityTest]
        public IEnumerator ReportCapture_ReportsProperJson()
        {
            var sensorHandle = RegisterSensor("camera", "camera", "", 0, CaptureTriggerMode.Scheduled, 1, 0);
            var sensor = CreateMocRgbCapture();
            sensorHandle.ReportSensor(sensor);

            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);

            yield return null;
            DatasetCapture.Instance.ResetSimulation();
            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;

            Assert.IsFalse(sensorHandle.IsValid);

            Assert.NotNull(collector.currentRun);
            Assert.AreEqual(collector.currentRun.frames.Count, 1);
            Assert.NotNull(collector.currentRun.frames.First().sensors);
            Assert.AreEqual(collector.currentRun.frames.First().sensors.Count(), 1);

            var rgb = collector.currentRun.frames.First().sensors.First() as RgbSensor;
            Assert.NotNull(rgb);

            AssertUtils.AreEqual(rgb, sensor);
        }

        [UnityTest]
        public IEnumerator StartNewSequence_ProperlyIncrementsSequence()
        {
            var timingsExpected = new(int seq, int step, int timestamp)[]
            {
                (0, 0, 0),
                (0, 1, 2),
                (1, 0, 0),
                (1, 1, 2)
            };

            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);

            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 2, 0);
            var sensor = new RgbSensor();

            Assert.IsTrue(sensorHandle.ShouldCaptureThisFrame);
            sensorHandle.ReportSensor(sensor);
            yield return null;

            Assert.IsTrue(sensorHandle.ShouldCaptureThisFrame);
            sensorHandle.ReportSensor(sensor);
            yield return null;

            DatasetCapture.Instance.StartNewSequence();
            Assert.IsTrue(sensorHandle.ShouldCaptureThisFrame);
            sensorHandle.ReportSensor(sensor);
            yield return null;
            Assert.IsTrue(sensorHandle.ShouldCaptureThisFrame);
            sensorHandle.ReportSensor(sensor);

            DatasetCapture.Instance.ResetSimulation();
            Assert.IsFalse(sensorHandle.IsValid);

            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;

            Debug.Log("after watcher");

            Assert.NotNull(collector.currentRun);
            Assert.AreEqual(timingsExpected.Length, collector.currentRun.TotalFrames);

            var i = 0;
            foreach (var (seq, step, timestamp) in timingsExpected)
            {
                var collected = collector.currentRun.frames[i++];
                Assert.AreEqual(seq, collected.sequence);
                Assert.AreEqual(step, collected.step);
                Assert.AreEqual(timestamp, collected.timestamp);
            }
        }

        [UnityTest]
        public IEnumerator ReportAnnotation_AddsProperJsonToCapture()
        {
            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);

            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);
            var sensor = new RgbSensor();
            sensorHandle.ReportSensor(sensor);

            var def = new SemanticSegmentationLabeler.SemanticSegmentationDefinition();
            var annotation = new SemanticSegmentationLabeler.SemanticSegmentation
            {
                Id = def.id,
                annotationType = def.annotationType,
                description = def.description,
                imageFormat = "png",
                sensorId = sensorHandle.Id,
                dimension = new Vector2(0, 0),
                buffer = new byte[0],
                instances = new List<SemanticSegmentationLabeler.SemanticSegmentationDefinition.DefinitionEntry>()
            };

            DatasetCapture.Instance.RegisterAnnotationDefinition(def);
            sensorHandle.ReportAnnotation(def, annotation);

            yield return null;
            DatasetCapture.Instance.ResetSimulation();
            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;

            Assert.IsFalse(sensorHandle.IsValid);

            Assert.NotNull(collector.currentRun);
            Assert.AreEqual(collector.currentRun.frames.Count, 1);
            Assert.NotNull(collector.currentRun.frames.First().sensors);
            Assert.AreEqual(collector.currentRun.frames.First().sensors.Count(), 1);

            var rgb = collector.currentRun.frames.First().sensors.First() as RgbSensor;
            Assert.NotNull(rgb);

            AssertUtils.AreEqual(rgb, sensor);

            Assert.NotNull(rgb.annotations);
            Assert.AreEqual(1, rgb.annotations.Count());
            var seg = rgb.annotations.First() as SemanticSegmentationLabeler.SemanticSegmentation;
            Assert.NotNull(seg);

            Assert.AreEqual(SemanticSegmentationLabeler.annotationId, seg.Id);

            // TODO add some more...
        }

        class TestDef : AnnotationDefinition
        {
            public TestDef() : base("test", "blah", "test") { }
        }

        class TestDef2 : AnnotationDefinition
        {
            public TestDef2()
                : base("test2", "even more blah", "test2") { }
        }

        class TestAnnotation : Annotation
        {
            public struct Entry
            {
                public string a;
                public int b;
            }

            public List<Entry> entries = new List<Entry>();
        }

        [UnityTest]
        public IEnumerator ReportAnnotationValues_ReportsProperJson()
        {
            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);

            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);
            var sensor = new RgbSensor();
            sensorHandle.ReportSensor(sensor);

            var def = new TestDef();
            var ann = new TestAnnotation()
            {
                entries = new List<TestAnnotation.Entry>()
                {
                    new TestAnnotation.Entry { a = "a string", b = 10 },
                    new TestAnnotation.Entry { a = "a second string", b = 20 }
                }
            };

            DatasetCapture.Instance.RegisterAnnotationDefinition(def);
            sensorHandle.ReportAnnotation(def, ann);

            yield return null;
            DatasetCapture.Instance.ResetSimulation();
            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;

            Assert.IsFalse(sensorHandle.IsValid);

            var rgb = collector.currentRun.frames.First().sensors.First() as RgbSensor;
            Assert.NotNull(rgb);

            Assert.NotNull(rgb.annotations);
            Assert.AreEqual(1, rgb.annotations.Count());
            var tAnn = rgb.annotations.First() as TestAnnotation;
            Assert.NotNull(tAnn);

            Assert.AreEqual(2, tAnn.entries.Count);

            Assert.AreEqual("a string", tAnn.entries[0].a);
            Assert.AreEqual(10, tAnn.entries[0].b);
            Assert.AreEqual("a second string", tAnn.entries[1].a);
            Assert.AreEqual(20, tAnn.entries[1].b);
        }

        [UnityTest]
        public IEnumerator ReportAnnotationFile_WhenCaptureNotExpected_Throws()
        {
            var def = new TestDef();
            DatasetCapture.Instance.RegisterAnnotationDefinition(def);
            var sensorHandle = RegisterSensor("camera", "", "", 100, CaptureTriggerMode.Scheduled, 1, 0);
            Assert.Throws<InvalidOperationException>(() => sensorHandle.ReportAnnotation(def, null));

            DatasetCapture.Instance.ResetSimulation();
            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;
        }

        [Test]
        public void ReportAnnotationValues_WhenCaptureNotExpected_Throws()
        {
            var def = new TestDef();
            DatasetCapture.Instance.RegisterAnnotationDefinition(def);
            var ann = new TestAnnotation()
            {
                entries = new List<TestAnnotation.Entry>()
                {
                    new TestAnnotation.Entry { a = "a string", b = 10 },
                    new TestAnnotation.Entry { a = "a second string", b = 20 }
                }
            };
            var sensorHandle = RegisterSensor("camera", "", "", 100, CaptureTriggerMode.Scheduled, 1, 0);
            Assert.Throws<InvalidOperationException>(() => sensorHandle.ReportAnnotation(def, ann));
            DatasetCapture.Instance.ResetSimulation();
        }

        [Test]
        public void ReportAnnotationAsync_WhenCaptureNotExpected_Throws()
        {
            var def = new TestDef();
            DatasetCapture.Instance.RegisterAnnotationDefinition(def);
            var ann = new TestAnnotation()
            {
                entries = new List<TestAnnotation.Entry>()
                {
                    new TestAnnotation.Entry { a = "a string", b = 10 },
                    new TestAnnotation.Entry { a = "a second string", b = 20 }
                }
            };
            var sensorHandle = RegisterSensor("camera", "", "", 100, CaptureTriggerMode.Scheduled, 1, 0);
            Assert.Throws<InvalidOperationException>(() => sensorHandle.ReportAnnotationAsync(def));
        }
#if false
        [Test]
        public void ResetSimulation_WithUnreportedAnnotationAsync_LogsError() // TODO, need to think about this one
        {
            var def = new TestDef();
            DatasetCapture.Instance.RegisterAnnotationDefinition(def);
            var ann = new TestAnnotation()
            {
                entries = new List<TestAnnotation.Entry>()
                {
                    new TestAnnotation.Entry { a = "a string", b = 10 },
                    new TestAnnotation.Entry { a = "a second string", b = 20 }
                }
            };
            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);
            sensorHandle.ReportAnnotationAsync(def);
            DatasetCapture.Instance.ResetSimulation();
            LogAssert.Expect(LogType.Error, new Regex("Simulation ended with pending .*"));

            // var annotationDefinition = DatasetCapture.RegisterAnnotationDefinition("");
            // var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);
            // sensorHandle.ReportAnnotationAsync(annotationDefinition);
            // DatasetCapture.ResetSimulation();
            // LogAssert.Expect(LogType.Error, new Regex("Simulation ended with pending .*"));
        }
#endif
        [Test]
        public void ResetSimulation_CallsSimulationEnding()
        {
            var timesCalled = 0;
            DatasetCapture.Instance.SimulationEnding += () => timesCalled++;
            DatasetCapture.Instance.ResetSimulation();
            DatasetCapture.Instance.ResetSimulation();
            Assert.AreEqual(2, timesCalled);
        }

        [UnityTest]
        public IEnumerator AnnotationAsyncInvalid_TimesOut()
        {
            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);

            SimulationState.TimeOutFrameCount = 100;

            var def = new TestDef();
            DatasetCapture.Instance.RegisterAnnotationDefinition(def);
            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);
            var asyncAnnotation = sensorHandle.ReportAnnotationAsync(def);
            Assert.IsTrue(asyncAnnotation.IsValid());

            DatasetCapture.Instance.ResetSimulation();
            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;

            LogAssert.Expect(LogType.Error, new Regex("A frame has timed out and is being removed.*"));
            Assert.IsFalse(asyncAnnotation.IsValid());
        }

        [UnityTest]
        public IEnumerator AnnotationAsyncIsValid_ReturnsProperValue()
        {
            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);

            SimulationState.TimeOutFrameCount = 10;

            LogAssert.ignoreFailingMessages = true; //we are not worried about timing out

            var def = new TestDef();
            DatasetCapture.Instance.RegisterAnnotationDefinition(def);
            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);
            var asyncAnnotation = sensorHandle.ReportAnnotationAsync(def);
            Assert.IsTrue(asyncAnnotation.IsValid());

            DatasetCapture.Instance.ResetSimulation();
            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;

            Assert.IsFalse(asyncAnnotation.IsValid());
            SimulationState.TimeOutFrameCount = 6000;
        }

        [UnityTest]
        public IEnumerator AnnotationAsyncReportValue_ReportsProperJson()
        {
            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);

            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);
            var sensor = new RgbSensor();
            sensorHandle.ReportSensor(sensor);

            var def = new TestDef();
            var ann = new TestAnnotation()
            {
                entries = new List<TestAnnotation.Entry>()
                {
                    new TestAnnotation.Entry { a = "a string", b = 10 },
                    new TestAnnotation.Entry { a = "a second string", b = 20 }
                }
            };

            DatasetCapture.Instance.RegisterAnnotationDefinition(def);
            var asyncFuture = sensorHandle.ReportAnnotationAsync(def);

            Assert.IsTrue(asyncFuture.IsPending());
            asyncFuture.Report(ann);
            Assert.IsFalse(asyncFuture.IsPending());
            yield return null;                            // TODO why does removing this cause us to spiral out for eternity
            DatasetCapture.Instance.ResetSimulation();

            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;

            Assert.IsFalse(sensorHandle.IsValid);

            var rgb = collector.currentRun.frames.First().sensors.First() as RgbSensor;
            Assert.NotNull(rgb);

            Assert.NotNull(rgb.annotations);
            Assert.AreEqual(1, rgb.annotations.Count());
            var tAnn = rgb.annotations.First() as TestAnnotation;
            Assert.NotNull(tAnn);

            Assert.AreEqual(2, tAnn.entries.Count);

            Assert.AreEqual("a string", tAnn.entries[0].a);
            Assert.AreEqual(10, tAnn.entries[0].b);
            Assert.AreEqual("a second string", tAnn.entries[1].a);
            Assert.AreEqual(20, tAnn.entries[1].b);
        }

        [UnityTest]
        public IEnumerator AnnotationAsyncReportResult_FindsCorrectPendingCaptureAfterStartingNewSequence()
        {
            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);

            var def = new TestDef();
            DatasetCapture.Instance.RegisterAnnotationDefinition(def);

            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);

            // Record one capture for this frame
            var sensor = new RgbSensor();
            sensorHandle.ReportSensor(sensor);

            // Wait one frame
            yield return null;

            // Reset the capture step
            DatasetCapture.Instance.StartNewSequence();

            // Record a new capture on different frame that has the same step (0) as the first capture
            sensorHandle.ReportSensor(sensor);

            var ann = new TestAnnotation()
            {
                entries = new List<TestAnnotation.Entry>()
                {
                    new TestAnnotation.Entry { a = "a string", b = 10 },
                    new TestAnnotation.Entry { a = "a second string", b = 20 }
                }
            };

            // Confirm that the annotation correctly skips the first pending capture to write to the second
            var asyncAnnotation = sensorHandle.ReportAnnotationAsync(def);
            Assert.DoesNotThrow(() => asyncAnnotation.Report(ann));
            sensorHandle.ReportSensor(sensor);
            DatasetCapture.Instance.ResetSimulation();

            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;
        }


        [Test]
        public void CreateAnnotation_MultipleTimes_WritesProperTypeOnce()
        {
            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);

            var def1 = new TestDef();
            var def2 = new TestDef();
            DatasetCapture.Instance.RegisterAnnotationDefinition(def1);
            DatasetCapture.Instance.RegisterAnnotationDefinition(def2);

            DatasetCapture.Instance.ResetSimulation();

            Assert.AreNotEqual(def1.id, def2.id);
            Assert.AreEqual("test", def1.id);
            Assert.AreEqual("test_0", def2.id);

            Assert.AreEqual(2, collector.annotationDefinitions.Count);
            Assert.AreEqual(def1.id, collector.annotationDefinitions[0].id);
            Assert.AreEqual(def2.id, collector.annotationDefinitions[1].id);
        }

        [Test]
        public void CreateAnnotation_MultipleTimesWithDifferentParameters_WritesProperTypes()
        {
            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);
            var def1 = new TestDef();
            var def2 = new TestDef2();

            DatasetCapture.Instance.RegisterAnnotationDefinition(def1);
            DatasetCapture.Instance.RegisterAnnotationDefinition(def2);

            DatasetCapture.Instance.ResetSimulation();
            Assert.AreNotEqual(def1.id, def2.id);
            Assert.AreEqual("test", def1.id);
            Assert.AreEqual("test2", def2.id);

            Assert.AreEqual(2, collector.annotationDefinitions.Count);
            Assert.AreEqual(def1.id, collector.annotationDefinitions[0].id);
            Assert.AreEqual(def2.id, collector.annotationDefinitions[1].id);
        }
#if false
        class TestMetricDef : MetricDefinition
        {
            public TestMetricDef() : base("test", "counting blahs") {}
        }

        [Test]
        public void ReportMetricValues_WhenCaptureNotExpected_Throws()
        {
            var def = new TestMetricDef();
            DatasetCapture.Instance.RegisterMetric(def);
            var sensorHandle = RegisterSensor("camera", "", "", 100, CaptureTriggerMode.Scheduled, 1, 0);
            Assert.Throws<InvalidOperationException>(() => sensorHandle.ReportMetric(def, null));
        }

        [Test]
        public void ReportMetricAsync_WhenCaptureNotExpected_Throws()
        {
            var def = new TestMetricDef();
            DatasetCapture.Instance.RegisterMetric(def);
            var sensorHandle = RegisterSensor("camera", "", "", 100, CaptureTriggerMode.Scheduled, 1, 0);
            Assert.Throws<InvalidOperationException>(() => sensorHandle.ReportMetricAsync(def));
        }

        [Test]
        public void ResetSimulation_WithUnreportedMetricAsync_LogsError()
        {
            // TODO I don't think this test will happen anymore

            var def = new TestMetricDef();
            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);
            sensorHandle.ReportMetricAsync(def);
            DatasetCapture.Instance.ResetSimulation();
            LogAssert.Expect(LogType.Error, new Regex("Simulation ended with pending .*"));
        }

        [Test]
        public void MetricAsyncIsValid_ReturnsProperValue()
        {
            LogAssert.ignoreFailingMessages = true; //we aren't worried about "Simulation ended with pending..."
            var def = new TestMetricDef();
            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);
            var asyncMetric = sensorHandle.ReportMetricAsync(def);
            Assert.IsTrue(asyncMetric.IsValid());
            DatasetCapture.Instance.ResetSimulation();
            Assert.IsFalse(asyncMetric.IsValid());
        }

        public enum MetricTarget
        {
            Global,
            Capture,
            Annotation
        }

        class TestMetric : Metric
        {
            public int[] values;
        }

        [UnityTest]
        public IEnumerator MetricReportValues_WithNoReportsInFrames_DoesNotIncrementStep()
        {
            // THIS TEST IS NOT WORKING....

            var tm = new TestMetric
            {
                values = new[] { 1 }
            };


            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);

            var def = new TestMetricDef();
            DatasetCapture.Instance.RegisterMetric(def);

            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);

            yield return null;
            yield return null;
            yield return null;
            sensorHandle.ReportMetric(def, tm);
            DatasetCapture.Instance.ResetSimulation();

            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;


            // Record one capture for this frame
            var sensor = new RgbSensor();
            sensorHandle.ReportSensor(sensor);

            // Wait one frame
            yield return null;

            // Reset the capture step
            DatasetCapture.Instance.StartNewSequence();

            yield return null;

            // Record a new capture on different frame that has the same step (0) as the first capture
            sensorHandle.ReportSensor(sensor);

            dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;

//            var text = File.ReadAllText(Path.Combine(DatasetCapture.OutputDirectory, "metrics_000.json"));
//            StringAssert.Contains(expectedLine, text);
        }

        [UnityTest]
        public IEnumerator SensorHandleReportMetric_BeforeReportCapture_ReportsProperJson()
        {
            var tm = new TestMetric
            {
                values = new[] { 1 }
            };


            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);

            // DatasetCapture.Instance.automaticShutdown = false;

            var def = new TestMetricDef();
            DatasetCapture.Instance.RegisterMetric(def);

            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);





            // var values = new[] { 1 };
            //
            // var expectedLine = @"""step"": 0";

            // var metricDefinition = DatasetCapture.RegisterMetricDefinition("");
            // var sensor = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);

            yield return null;
            sensorHandle.ReportMetric(def, tm);
            var sensor = new RgbSensor();
            sensorHandle.ReportSensor(sensor);


            //sensorHandle.ReportCapture("file", new SensorSpatialData(Pose.identity, Pose.identity, null, null));
            DatasetCapture.Instance.ResetSimulation();

            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;

            var first = collector.currentRun.frames.First();
            Assert.NotNull(first);
            var foundSensor = first.sensors.First();
            Assert.NotNull(foundSensor);
            Assert.NotNull(foundSensor.metrics);
            Assert.NotZero(foundSensor.metrics.Count());
        }

        class TestMetric2 : Metric
        {
            public struct Entry
            {
                public string a;
                public int b;
            }

            public Entry[] values;
        }

        [UnityTest]
        public IEnumerator MetricAsyncReportValues_ReportsProperJson(
            [Values(MetricTarget.Global, MetricTarget.Capture, MetricTarget.Annotation)] MetricTarget metricTarget,
            [Values(true, false)] bool async,
            [Values(true, false)] bool asStringJsonArray)
        {
            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);

            // DatasetCapture.Instance.automaticShutdown = false;

            var values = new[]
            {
                new TestMetric2.Entry
                {
                    a = "a string",
                    b = 10
                },
                new TestMetric2.Entry
                {
                    a = "a second string",
                    b = 20
                },
            };

            var metric = new TestMetric2
            {
                values = values
            };

            var metDef = new TestMetricDef();
            DatasetCapture.Instance.RegisterMetric(metDef);

            var sensorHandle = RegisterSensor("camera", "", "", 0, CaptureTriggerMode.Scheduled, 1, 0);

            var sensor = new RgbSensor();
            sensorHandle.ReportSensor(sensor);

            var annDef = new TestDef();

            DatasetCapture.Instance.RegisterAnnotationDefinition(annDef);

            if (async)
            {
                AsyncMetricFuture asyncMetric;
                switch (metricTarget)
                {
                    case MetricTarget.Global:
                        asyncMetric = sensorHandle.ReportMetricAsync(metDef);
                        break;
                    case MetricTarget.Capture:
                        asyncMetric = sensorHandle.ReportMetricAsync(metDef);
                        break;
                    case MetricTarget.Annotation:
                        asyncMetric = sensorHandle.ReportMetricAsync(metDef);
                        break;
                    default:
                        throw new Exception("unsupported");
                }

                Assert.IsTrue(asyncMetric.IsPending());
                asyncMetric.Report(metric);
                Assert.IsFalse(asyncMetric.IsPending());
            }
            else
            {
                switch (metricTarget)
                {
                    case MetricTarget.Global:
                        sensorHandle.ReportMetric(metDef, metric);
                        break;
                    case MetricTarget.Capture:
                        sensorHandle.ReportMetric(metDef, metric);
                        break;
                    case MetricTarget.Annotation:
                        sensorHandle.ReportMetric(metDef, metric);
                        break;
                    default:
                        throw new Exception("unsupported");
                }
            }
            DatasetCapture.Instance.ResetSimulation();

            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;

            var first = collector.currentRun.frames.First();
            Assert.NotNull(first);
            var foundSensor = first.sensors.First();
            Assert.NotNull(foundSensor);
            Assert.NotNull(foundSensor.metrics);
            Assert.NotZero(foundSensor.metrics.Count());

            var m = foundSensor.metrics.First() as TestMetric2;
            Assert.NotNull(m);
            Assert.Equals(2, m.values.Count());

            Assert.AreEqual("a string", m.values[0].a);
            Assert.AreEqual(10, m.values[0].b);
            Assert.AreEqual("a second string", m.values[1].a);
            Assert.AreEqual(20, m.values[1].b);
        }

        class MetDef1 : MetricDefinition
        {
            public MetDef1()
            {
                id = "name";
                description = "name";
            }
        }

        class MetDef2 : MetricDefinition
        {
            public MetDef2()
            {
                id = "name2";
                description = "name2";
            }
        }


        [Test]
        public void CreateMetric_MultipleTimesWithDifferentParameters_WritesProperTypes()
        {
//             var metricDefinitionGuid = new Guid(10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
//
//             var metricDefinitionsJsonExpected =
//                 $@"{{
//   ""version"": ""{DatasetCapture.SchemaVersion}"",
//   ""metric_definitions"": [
//     {{
//       ""id"": <guid>,
//       ""name"": ""name""
//     }},
//     {{
//       ""id"": <guid>,
//       ""name"": ""name2"",
//       ""description"": ""description""
//     }}
//   ]
// }}";
            var md1 = new MetDef1();
            var md2 = new MetDef2();
            var md3 = new MetDef1();

            DatasetCapture.Instance.RegisterMetric(md1);
            DatasetCapture.Instance.RegisterMetric(md3);
            DatasetCapture.Instance.RegisterMetric(md2);

            DatasetCapture.Instance.ResetSimulation();

            Assert.AreEqual("name", md1.id);
            Assert.AreEqual("name2", md2.id);
            Assert.AreNotEqual("name", md3);
            Assert.AreEqual("name_0", md3);
        }

        struct TestSpec
        {
            public int label_id;
            public string label_name;
            public int[] pixel_value;
        }

        public enum AdditionalInfoKind
        {
            Annotation,
            Metric
        }

        class A1 : AnnotationDefinition
        {
            public TestSpec[] specValues;
        }

        class M1 : MetricDefinition
        {
            public TestSpec[] specValues;
        }

        [Test]
        public void CreateAnnotationOrMetric_WithSpecValues_WritesProperTypes(
            [Values(AdditionalInfoKind.Annotation, AdditionalInfoKind.Metric)] AdditionalInfoKind additionalInfoKind)
        {
            var specValues = new[]
            {
                new TestSpec
                {
                    label_id = 1,
                    label_name = "sky",
                    pixel_value = new[] { 1, 2, 3}
                },
                new TestSpec
                {
                    label_id = 2,
                    label_name = "sidewalk",
                    pixel_value = new[] { 4, 5, 6}
                }
            };

            var collector = new CollectEndpoint();
            DatasetCapture.SetEndpoint(collector);

            // DatasetCapture.Instance.automaticShutdown = false;

            if (additionalInfoKind == AdditionalInfoKind.Annotation)
            {
                var ad = new A1
                {
                    specValues = specValues
                };
                DatasetCapture.Instance.RegisterAnnotationDefinition(ad);
            }
            else
            {
                var md = new M1
                {
                    specValues = specValues
                };

                DatasetCapture.Instance.RegisterMetric(md);
            }

            DatasetCapture.Instance.ResetSimulation();

            if (additionalInfoKind == AdditionalInfoKind.Annotation)
            {
                Assert.AreEqual(1, collector.annotationDefinitions.Count);
                var a = collector.annotationDefinitions.First() as A1;
                Assert.NotNull(a);
                Assert.AreEqual(2, a.specValues.Length);

                Assert.AreEqual(1, a.specValues[0].label_id);
                Assert.AreEqual("sky", a.specValues[0].label_name);
                Assert.AreEqual(3, a.specValues[0].pixel_value.Count());
                Assert.AreEqual(1, a.specValues[0].pixel_value[0]);
                Assert.AreEqual(2, a.specValues[0].pixel_value[1]);
                Assert.AreEqual(3, a.specValues[0].pixel_value[2]);

                Assert.AreEqual(2, a.specValues[1].label_id);
                Assert.AreEqual("sidewalk", a.specValues[1].label_name);
                Assert.AreEqual(3, a.specValues[1].pixel_value.Count());
                Assert.AreEqual(4, a.specValues[1].pixel_value[0]);
                Assert.AreEqual(5, a.specValues[1].pixel_value[1]);
                Assert.AreEqual(6, a.specValues[1].pixel_value[2]);
            }
            else
            {
                Assert.AreEqual(1, collector.metricDefinitions.Count);
                var a = collector.metricDefinitions.First() as M1;
                Assert.NotNull(a);
                Assert.AreEqual(2, a.specValues.Length);

                Assert.AreEqual(1, a.specValues[0].label_id);
                Assert.AreEqual("sky", a.specValues[0].label_name);
                Assert.AreEqual(3, a.specValues[0].pixel_value.Count());
                Assert.AreEqual(1, a.specValues[0].pixel_value[0]);
                Assert.AreEqual(2, a.specValues[0].pixel_value[1]);
                Assert.AreEqual(3, a.specValues[0].pixel_value[2]);

                Assert.AreEqual(2, a.specValues[1].label_id);
                Assert.AreEqual("sidewalk", a.specValues[1].label_name);
                Assert.AreEqual(3, a.specValues[1].pixel_value.Count());
                Assert.AreEqual(4, a.specValues[1].pixel_value[0]);
                Assert.AreEqual(5, a.specValues[1].pixel_value[1]);
                Assert.AreEqual(6, a.specValues[1].pixel_value[2]);
            }
        }
#endif
    }
}
