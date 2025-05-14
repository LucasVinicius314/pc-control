import 'dart:async';

import 'package:client/model/control_screen_parameters.dart';
import 'package:client/screen/control_screen.dart';
import 'package:flutter/material.dart';
import 'package:uni_links/uni_links.dart' as uni_links;

class HomeScreen extends StatefulWidget {
  const HomeScreen({
    super.key,
  });

  static const route = '/';

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  final _controller = TextEditingController();

  StreamSubscription? _sub;

  Future<void> _handleInitialUri() async {
    try {
      final uri = await uni_links.getInitialUri();
      if (uri != null) {
        final ipAddress = uri.queryParameters['ipAddress'] ?? '';
        if (ipAddress == '') {
          return;
        }

        await _pushControlScreen(ipAddress: ipAddress);
      }
    } catch (e) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  void _initDeepLinkListener() {
    _sub = uni_links.uriLinkStream.listen((Uri? uri) async {
      if (ModalRoute.of(context)?.isCurrent != true) {
        return;
      }

      if (uri != null) {
        final ipAddress = uri.queryParameters['ipAddress'] ?? '';
        if (ipAddress == '') {
          return;
        }

        await _pushControlScreen(ipAddress: ipAddress);
      }
    }, onError: (err) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(err.toString())));
    });
  }

  Future<void> _pushControlScreen({
    required String ipAddress,
  }) async {
    await Navigator.of(context).pushNamed(
      ControlScreen.route,
      arguments: ControlScreenParameters(ipAddress: ipAddress),
    );
  }

  @override
  void initState() {
    super.initState();

    _handleInitialUri();
    _initDeepLinkListener();
  }

  @override
  void dispose() {
    super.dispose();
    _sub?.cancel();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('PC Control')),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            TextFormField(
              controller: _controller,
              onFieldSubmitted: (value) async {
                await _pushControlScreen(ipAddress: _controller.text);
              },
            ),
          ],
        ),
      ),
    );
  }
}
