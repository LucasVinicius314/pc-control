import 'dart:convert';

import 'package:client/enum/app_event_type.dart';

class AppEvent {
  const AppEvent({
    required this.data,
    required this.type,
  });

  factory AppEvent.getFeed({
    required int height,
    required int width,
    required int x,
    required int y,
  }) {
    return AppEvent(
      data: {
        'height': height,
        'width': width,
        'x': x,
        'y': y,
      },
      type: AppEventType.getFeed,
    );
  }

  factory AppEvent.interact({
    required String code,
  }) {
    return AppEvent(
      data: {
        'code': code,
      },
      type: AppEventType.interact,
    );
  }

  factory AppEvent.fromJson(Map<String, dynamic> value) {
    return AppEvent(
      data: value['data'],
      type: AppEventType.fromString(value['type']),
    );
  }

  factory AppEvent.fromString(String value) {
    return AppEvent.fromJson(jsonDecode(value));
  }

  final Map<String, dynamic> data;
  final AppEventType type;

  Map<String, dynamic> toJson() {
    return {
      'data': data,
      'type': type.value,
    };
  }

  @override
  String toString() {
    return jsonEncode(toJson());
  }
}
