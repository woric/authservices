﻿using Kentor.AuthServices.WebSso;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Web;
using System.IdentityModel.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using Kentor.AuthServices.Saml2P;
using System.IdentityModel.Tokens;
using Kentor.AuthServices.Internal;
using System.Reflection;
using Kentor.AuthServices.Tests.Helpers;
using System.Xml;

namespace Kentor.AuthServices.Tests.WebSSO
{
    [TestClass]
    public class Saml2ArtifactBindingTests
    {
        [TestMethod]
        public void Saml2ArtifactBinding_CanUnbind_Nullcheck()
        {
            Saml2Binding.Get(Saml2BindingType.Artifact)
                .Invoking(b => b.CanUnbind(null))
                .ShouldThrow<ArgumentNullException>()
                .And.ParamName.Should().Be("request");
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Unbind_Nullcheck()
        {
            Saml2Binding.Get(Saml2BindingType.Artifact)
                .Invoking(b => b.Unbind(null, null))
                .ShouldThrow<ArgumentNullException>()
                .And.ParamName.Should().Be("request");
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Unbind_FromGet()
        {
            var issuer = new EntityId("https://idp.example.com");
            var artifact = Uri.EscapeDataString(
                Convert.ToBase64String(
                    Saml2ArtifactBinding.CreateArtifact(issuer, 0x1234)));

            var relayState = "relayState";

            var r = new HttpRequestData(
                "GET",
                new Uri($"http://example.com/path/acs?SAMLart={artifact}&RelayState={relayState}"),
                null,
                null,
                new StoredRequestState(issuer, null, null, null));

            StubServer.LastArtifactResolutionSoapActionHeader = null;

            var result = Saml2Binding.Get(Saml2BindingType.Artifact).Unbind(r, StubFactory.CreateOptions());

            var xmlDocument = new XmlDocument() { PreserveWhitespace = true };
            xmlDocument.LoadXml("<message>   <child-node /> </message>");

            var expected = new UnbindResult(xmlDocument.DocumentElement, relayState, TrustLevel.None);

            result.ShouldBeEquivalentTo(expected);
            StubServer.LastArtifactResolutionSoapActionHeader.Should().Be(
                "http://www.oasis-open.org/committees/security");
            StubServer.LastArtifactResolutionWasSigned.Should().BeFalse();
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Unbind_FromGet_ArtifactIsntHashOfEntityId()
        {
            var issuer = new EntityId("https://idp.example.com");
            var artifact = Uri.EscapeDataString(
                Convert.ToBase64String(
                    Saml2ArtifactBinding.CreateArtifact(
                        new EntityId("https://this.entityid.is.invalid"),
                        0x1234)));

            var relayState = "relayState";

            var r = new HttpRequestData(
                "GET",
                new Uri($"http://example.com/path/acs?SAMLart={artifact}&RelayState={relayState}"),
                null,
                null,
                new StoredRequestState(issuer, null, null, null));

            StubServer.LastArtifactResolutionSoapActionHeader = null;

            var result = Saml2Binding.Get(Saml2BindingType.Artifact).Unbind(r, StubFactory.CreateOptions());

            var xmlDocument = new XmlDocument() { PreserveWhitespace = true };
            xmlDocument.LoadXml("<message>   <child-node /> </message>");

            var expected = new UnbindResult(xmlDocument.DocumentElement, relayState, TrustLevel.None);

            result.ShouldBeEquivalentTo(expected);
            StubServer.LastArtifactResolutionSoapActionHeader.Should().Be(
                "http://www.oasis-open.org/committees/security");
            StubServer.LastArtifactResolutionWasSigned.Should().BeFalse();
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Unbind_FromGet_SignsArtifactResolve()
        {
            var issuer = new EntityId("https://idp.example.com");
            var artifact = Uri.EscapeDataString(
                Convert.ToBase64String(
                    Saml2ArtifactBinding.CreateArtifact(issuer, 0x1234)));

            var r = new HttpRequestData(
                "GET",
                new Uri($"http://example.com/path/acs?SAMLart={artifact}"));

            var options = StubFactory.CreateOptions();
            options.SPOptions.ServiceCertificates.Add(new ServiceCertificate
            {
                Certificate = SignedXmlHelper.TestCert
            });

            var result = Saml2Binding.Get(Saml2BindingType.Artifact).Unbind(r, options);

            StubServer.LastArtifactResolutionWasSigned.Should().BeTrue();
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Unbind_FromGetWithoutRelayState()
        {
            var issuer = new EntityId("https://idp.example.com");
            var artifact = Uri.EscapeDataString(
                Convert.ToBase64String(
                    Saml2ArtifactBinding.CreateArtifact(issuer, 0x1234)));

            var r = new HttpRequestData(
                "GET",
                new Uri($"http://example.com/path/acs?SAMLart={artifact}"));

            StubServer.LastArtifactResolutionSoapActionHeader = null;

            var result = Saml2Binding.Get(Saml2BindingType.Artifact).Unbind(r, StubFactory.CreateOptions());

            var xmlDocument = new XmlDocument() { PreserveWhitespace = true };
            xmlDocument.LoadXml("<message>   <child-node /> </message>");

            var expected = new UnbindResult(xmlDocument.DocumentElement, null, TrustLevel.None);

            result.ShouldBeEquivalentTo(expected);
            StubServer.LastArtifactResolutionSoapActionHeader.Should().Be(
                "http://www.oasis-open.org/committees/security");
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Unbind_FromPost()
        {
            var issuer = new EntityId("https://idp.example.com");
            var artifact = Convert.ToBase64String(
                    Saml2ArtifactBinding.CreateArtifact(issuer, 0x1234));

            var relayState = MethodBase.GetCurrentMethod().Name;

            var r = new HttpRequestData(
                "POST",
                new Uri("http://example.com"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLart", new[] { artifact }),
                    new KeyValuePair<string, string[]>("RelayState", new[] { relayState })
                },
                new StoredRequestState(issuer, null, null, null));

            StubServer.LastArtifactResolutionSoapActionHeader = null;

            var result = Saml2Binding.Get(Saml2BindingType.Artifact).Unbind(r, StubFactory.CreateOptions());

            var xmlDocument = new XmlDocument() { PreserveWhitespace = true };
            xmlDocument.LoadXml("<message>   <child-node /> </message>");

            var expected = new UnbindResult(xmlDocument.DocumentElement, relayState, TrustLevel.None);

            result.ShouldBeEquivalentTo(expected);
            StubServer.LastArtifactResolutionSoapActionHeader.Should().Be(
                "http://www.oasis-open.org/committees/security");
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Unbind_FromPostWithoutRelayState()
        {
            var issuer = new EntityId("https://idp.example.com");
            var artifact = Convert.ToBase64String(
                    Saml2ArtifactBinding.CreateArtifact(issuer, 0x1234));

            var r = new HttpRequestData(
                "POST",
                new Uri("http://example.com"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLart", new[] { artifact }),
                },
                null,
                null);

            StubServer.LastArtifactResolutionSoapActionHeader = null;

            var result = Saml2Binding.Get(Saml2BindingType.Artifact).Unbind(r, StubFactory.CreateOptions());

            var xmlDocument = new XmlDocument() { PreserveWhitespace = true };
            xmlDocument.LoadXml("<message>   <child-node /> </message>");

            var expected = new UnbindResult(xmlDocument.DocumentElement, null, TrustLevel.None);

            result.ShouldBeEquivalentTo(expected);
            StubServer.LastArtifactResolutionSoapActionHeader.Should().Be(
                "http://www.oasis-open.org/committees/security");
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Unbind_ThrowsOnUnknownHttpMethod()
        {
            var r = new HttpRequestData("PUT", new Uri("http://host"));

            Saml2Binding.Get(Saml2BindingType.Artifact)
                .Invoking(b => b.Unbind(r, null))
                .ShouldThrow<InvalidOperationException>()
                .WithMessage("Artifact binding can only use GET or POST http method, but found PUT");
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Bind()
        {
            var message = new Saml2MessageImplementation
            {
                DestinationUrl = new Uri("http://example.com/destination"),
                MessageName = "ShouldBeIgnored",
                RelayState = "ABC& needs escape",
                XmlData = "<XML />",
                Issuer = new EntityId("http://idp.example.com"),
            };

            var result = Saml2Binding.Get(Saml2BindingType.Artifact).Bind(message);

            var expected = new CommandResult
            {
                HttpStatusCode = HttpStatusCode.SeeOther
            };

            result.ShouldBeEquivalentTo(expected, opt => opt.Excluding(r => r.Location));

            result.Location.Query.Count(c => c == '=').Should().Be(2, "there are 2 params and = inside values should have been escaped");
            var query = HttpUtility.ParseQueryString(result.Location.Query);

            Uri.UnescapeDataString(query["RelayState"]).Should().Be(message.RelayState);

            var artifact = Convert.FromBase64String(
                Uri.UnescapeDataString(query["SAMLart"]));

            ISaml2Message storedMessage;
            Saml2ArtifactBinding.PendingMessages.TryRemove(artifact, out storedMessage)
                .Should().BeTrue();

            storedMessage.Should().BeSameAs(message);
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Bind_WithQueryInDestination()
        {
            var message = new Saml2MessageImplementation
            {
                DestinationUrl = new Uri("http://example.com/destination?q=a"),
                MessageName = "ShouldBeIgnored",
                RelayState = "ABC123",
                XmlData = "<XML />",
                Issuer = new EntityId("http://idp.example.com")
            };

            var result = Saml2Binding.Get(Saml2BindingType.Artifact).Bind(message);

            result.Location.Query.Trim('?').Contains("?").Should().BeFalse();
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Bind_WithoutRelayState()
        {
            var message = new Saml2MessageImplementation
            {
                DestinationUrl = new Uri("http://example.com/destination?q=a"),
                MessageName = "ShouldBeIgnored",
                XmlData = "<XML />",
                Issuer = new EntityId("http://idp.example.com")
            };

            Action a = () => Saml2Binding.Get(Saml2BindingType.Artifact).Bind(message);

            a.ShouldNotThrow();
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Bind_Nullcheck()
        {
            Saml2Binding.Get(Saml2BindingType.Artifact)
                .Invoking(b => b.Bind(null))
                .ShouldThrow<ArgumentNullException>("message");
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Bind_CreateArtifact()
        {
            var issuer = new EntityId("http://idp.example.com");
            var index = 0x1234;
            var artifact = Saml2ArtifactBinding.CreateArtifact(issuer, index);

            // Header
            artifact[0].Should().Be(0);
            artifact[1].Should().Be(4);

            //Endpoint index
            artifact[2].Should().Be(0x12);
            artifact[3].Should().Be(0x34);

            artifact.Length.Should().Be(44);

            var sourceID = new byte[20];
            Array.Copy(artifact, 4, sourceID, 0, 20);

            sourceID.ShouldBeEquivalentTo(
                SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(issuer.Id)));

            // Can't test a random value, but check it's not 0 all over.
            artifact.Skip(24).Count(c => c == 0).Should().BeLessThan(10);
        }

        [TestMethod]
        public void Saml2ArtifactBinding_Bind_CreateArtifact_NullcheckIssuer()
        {
            Action a = () => Saml2ArtifactBinding.CreateArtifact(null, 17);

            a.ShouldThrow<ArgumentNullException>()
                .And.ParamName.Should().Be("issuer");
        }

    }
}
