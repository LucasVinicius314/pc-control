enum AppEventType {
  getFeed(value: 'get-feed'),
  interact(value: 'interact');

  const AppEventType({
    required this.value,
  });

  factory AppEventType.fromString(String value) {
    return switch (value) {
      'get-feed' => getFeed,
      'interact' => interact,
      _ => throw 'invalid AppEventType($value)',
    };
  }

  final String value;
}
