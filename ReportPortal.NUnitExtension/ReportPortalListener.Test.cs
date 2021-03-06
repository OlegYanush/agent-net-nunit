﻿using ReportPortal.Client.Converters;
using ReportPortal.Client.Models;
using ReportPortal.Client.Requests;
using ReportPortal.NUnitExtension.EventArguments;
using ReportPortal.Shared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml;

namespace ReportPortal.NUnitExtension
{
    public partial class ReportPortalListener
    {
        public delegate void TestStartedHandler(object sender, TestItemStartedEventArgs e);

        public static event TestStartedHandler BeforeTestStarted;
        public static event TestStartedHandler AfterTestStarted;

        public void StartTest(XmlDocument xmlDoc)
        {
            try
            {
                var id = xmlDoc.SelectSingleNode("/*/@id").Value;
                var parentId = xmlDoc.SelectSingleNode("/*/@parentId").Value;
                var name = xmlDoc.SelectSingleNode("/*/@name").Value;
                var fullname = xmlDoc.SelectSingleNode("/*/@fullname").Value;

                var startTestRequest = new StartTestItemRequest
                {
                    StartTime = DateTime.UtcNow,
                    Name = name,
                    Type = TestItemType.Step
                };

                var beforeTestEventArg = new TestItemStartedEventArgs(Bridge.Service, startTestRequest);
                try
                {
                    if (BeforeTestStarted != null) BeforeTestStarted(this, beforeTestEventArg);
                }
                catch (Exception exp)
                {
                    Console.WriteLine("Exception was thrown in 'BeforeTestStarted' subscriber." + Environment.NewLine +
                                      exp);
                }
                if (!beforeTestEventArg.Canceled)
                {
                    var test = _suitesFlow[parentId].StartNewTestNode(startTestRequest);

                    _testFlowIds[id] = test;

                    _testFlowNames[fullname] = test;

                    try
                    {
                        if (AfterTestStarted != null)
                            AfterTestStarted(this, new TestItemStartedEventArgs(Bridge.Service, startTestRequest, test));
                    }
                    catch (Exception exp)
                    {
                        Console.WriteLine("Exception was thrown in 'AfterTestStarted' subscriber." + Environment.NewLine +
                                          exp);
                    }
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine("ReportPortal exception was thrown." + Environment.NewLine + exception);
            }
        }

        public delegate void TestFinishedHandler(object sender, TestItemFinishedEventArgs e);

        public static event TestFinishedHandler BeforeTestFinished;
        public static event TestFinishedHandler AfterTestFinished;

        public void FinishTest(XmlDocument xmlDoc)
        {
            try
            {
                var id = xmlDoc.SelectSingleNode("/*/@id").Value;
                var result = xmlDoc.SelectSingleNode("/*/@result").Value;
                var parentId = xmlDoc.SelectSingleNode("/*/@parentId");


                if (_testFlowIds.ContainsKey(id))
                {
                    var updateTestRequest = new UpdateTestItemRequest();

                    // adding categories to test
                    var categories = xmlDoc.SelectNodes("//properties/property[@name='Category']");
                    if (categories != null)
                    {
                        updateTestRequest.Tags = new List<string>();

                        foreach (XmlNode category in categories)
                        {
                            updateTestRequest.Tags.Add(category.Attributes["value"].Value);
                        }
                    }

                    // adding description to test
                    var description = xmlDoc.SelectSingleNode("//properties/property[@name='Description']");
                    if (description != null)
                    {
                        updateTestRequest.Description = description.Attributes["value"].Value;
                    }

                    if (updateTestRequest.Description != null || updateTestRequest.Tags != null)
                    {
                        _testFlowIds[id].Update(updateTestRequest);
                    }

                    // adding console output
                    var outputNode = xmlDoc.SelectSingleNode("//output");
                    if (outputNode != null)
                    {
                        _testFlowIds[id].Log(new AddLogItemRequest
                        {
                            Level = LogLevel.Trace,
                            Time = DateTime.UtcNow,
                            Text = "Test Output: " + Environment.NewLine + outputNode.InnerText
                        });
                    }

                    // adding failure message
                    var failureNode = xmlDoc.SelectSingleNode("//failure");
                    if (failureNode != null)
                    {
                        var failureMessage = failureNode.SelectSingleNode("./message").InnerText;
                        var failureStacktrace = failureNode.SelectSingleNode("./stack-trace").InnerText;

                        _testFlowIds[id].Log(new AddLogItemRequest
                        {
                            Level = LogLevel.Error,
                            Time = DateTime.UtcNow,
                            Text = failureMessage + Environment.NewLine + failureStacktrace
                        });
                    }

                    // finishing test
                    var finishTestRequest = new FinishTestItemRequest
                    {
                        EndTime = DateTime.UtcNow,
                        Status = _statusMap[result]
                    };

                    var eventArg = new TestItemFinishedEventArgs(Bridge.Service, finishTestRequest, _testFlowIds[id]);

                    try
                    {
                        if (BeforeTestFinished != null) BeforeTestFinished(this, eventArg);
                    }
                    catch (Exception exp)
                    {
                        Console.WriteLine("Exception was thrown in 'BeforeTestFinished' subscriber." +
                                          Environment.NewLine + exp);
                    }

                    _testFlowIds[id].Finish(finishTestRequest);

                    try
                    {
                        if (AfterTestFinished != null)
                            AfterTestFinished(this,
                                new TestItemFinishedEventArgs(Bridge.Service, finishTestRequest, _testFlowIds[id]));
                    }
                    catch (Exception exp)
                    {
                        Console.WriteLine("Exception was thrown in 'AfterTestFinished' subscriber." +
                                          Environment.NewLine + exp);
                    }
                }

            }
            catch (Exception exception)
            {
                Console.WriteLine("ReportPortal exception was thrown." + Environment.NewLine + exception);
            }
        }

        public void TestOutput(XmlDocument xmlDoc)
        {
            try
            {
                var fullTestName = xmlDoc.SelectSingleNode("/test-output/@testname").Value;
                var message = xmlDoc.SelectSingleNode("/test-output").InnerText;

                if (_testFlowNames.ContainsKey(fullTestName))
                {
                    AddLogItemRequest logRequest = null;
                    try
                    {
                        var sharedMessage = ModelSerializer.Deserialize<SharedLogMessage>(message);

                        logRequest = new AddLogItemRequest
                        {
                            Level = sharedMessage.Level,
                            Time = sharedMessage.Time,
                            TestItemId = sharedMessage.TestItemId,
                            Text = sharedMessage.Text
                        };
                        if (sharedMessage.Attach != null)
                        {
                            logRequest.Attach = new Client.Models.Attach
                            {
                                Name = sharedMessage.Attach.Name,
                                MimeType = sharedMessage.Attach.MimeType,
                                Data = sharedMessage.Attach.Data
                            };
                        }
                    }
                    catch (Exception)
                    {

                    }
                    
                    if (logRequest != null)
                    {
                        _testFlowNames[fullTestName].Log(logRequest);
                    }
                    else
                    {
                        _testFlowNames[fullTestName].Log(new AddLogItemRequest { Level = LogLevel.Info, Time = DateTime.UtcNow, Text = message});
                    }
                    
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine("ReportPortal exception was thrown." + Environment.NewLine + exception);
            }
        }
    }
}
