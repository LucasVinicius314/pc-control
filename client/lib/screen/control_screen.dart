import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:client/enum/app_event_type.dart';
import 'package:client/extension/list_extension.dart';
import 'package:client/model/app_event.dart';
import 'package:client/model/control_screen_parameters.dart';
import 'package:client/widget/pan_area.dart';
import 'package:flutter/material.dart';
import 'package:web_socket_channel/web_socket_channel.dart';

class ControlScreen extends StatefulWidget {
  const ControlScreen({
    super.key,
  });

  static const route = '/control';

  @override
  State<ControlScreen> createState() => _ControlScreenState();
}

class _ControlScreenState extends State<ControlScreen> {
  WebSocketChannel? _channel;

  final _getFeedStream = StreamController<Uint8List>();

  var _fps = 15;

  Widget _getCircle({
    required Color color,
    required double size,
    required Widget? child,
  }) {
    return Center(
      child: Container(
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(100),
          color: color,
        ),
        height: size,
        width: size,
        child: child,
      ),
    );
  }

  Widget _getPanSection() {
    return LayoutBuilder(
      builder: (context, constraints) {
        return Stack(
          children: [
            StreamBuilder<Uint8List>(
              stream: _getFeedStream.stream,
              builder: (context, snapshot) {
                if (snapshot.hasData) {
                  return Image.memory(
                    snapshot.data!,
                    gaplessPlayback: true,
                    fit: BoxFit.contain,
                  );
                }

                return const CircularProgressIndicator();
              },
            ),
            _getCircle(
              color: Colors.black,
              size: 10,
              child: _getCircle(
                color: Colors.white,
                size: 5,
                child: null,
              ),
            ),
            PanArea(
              fps: _fps,
              onPan: (offset) {
                _channel?.sink.add(
                  AppEvent.getFeed(
                    height: constraints.maxHeight.round(),
                    width: constraints.maxWidth.round(),
                    x: offset.dx.round(),
                    y: offset.dy.round(),
                  ).toString(),
                );
              },
              onPrimaryTap: () {
                _channel?.sink.add(
                  AppEvent.interact(
                    code: 'lmb',
                  ).toString(),
                );
              },
              onSecondaryTap: () {
                _channel?.sink.add(
                  AppEvent.interact(
                    code: 'rmb',
                  ).toString(),
                );
              },
            ),
          ],
        );
      },
    );
  }

  @override
  void initState() {
    super.initState();

    WidgetsBinding.instance.addPostFrameCallback((timeStamp) {
      final parameters = ModalRoute.of(context)?.settings.arguments;
      if (parameters is ControlScreenParameters) {
        _channel = WebSocketChannel.connect(
          Uri.parse('ws://${parameters.ipAddress}:48123/ws'),
        );

        _channel?.stream.listen((data) {
          final event = AppEvent.fromString(data);

          // TODO: fix, logs
          // print(['ws event', event]);

          switch (event.type) {
            case AppEventType.getFeed:
              _getFeedStream.add(base64Decode(event.data['content']));
            case AppEventType.interact:
              break;
          }
        });

        // TODO: fix, get display proportions
        _channel?.sink.add(
          AppEvent.getFeed(height: 100, width: 100, x: 100, y: 100).toString(),
        );
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('PC Control')),
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Expanded(child: _getPanSection()),
          const Divider(height: 1),
          Padding(
            padding: const EdgeInsets.all(8),
            child: Row(
              children: [
                (0, 'OFF'),
                (5, '5'),
                (15, '15'),
              ]
                  .map((e) {
                    return SizedBox(
                      width: 60,
                      child: Card(
                        clipBehavior: Clip.antiAlias,
                        child: InkWell(
                          onTap: () {
                            setState(() {
                              _fps = e.$1;
                            });
                          },
                          child: Padding(
                            padding: const EdgeInsets.all(8),
                            child: Text(e.$2, textAlign: TextAlign.center),
                          ),
                        ),
                      ),
                    );
                  })
                  .toList()
                  .alternateWidgets(const SizedBox(width: 8)),
            ),
          ),
        ],
      ),
    );
  }
}
