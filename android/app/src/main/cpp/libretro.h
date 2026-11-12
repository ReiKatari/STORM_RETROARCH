#ifndef STORM_LIBRETRO_H__
#define STORM_LIBRETRO_H__

#include <stdint.h>
#include <stddef.h>
#include <limits.h>
#include <stdarg.h>

#ifdef __cplusplus
extern "C" {
#else
#include <stdbool.h>
#endif

#ifndef RETRO_CALLCONV
#define RETRO_CALLCONV
#endif

#define RETRO_API_VERSION 1

#define RETRO_DEVICE_TYPE_SHIFT         8
#define RETRO_DEVICE_MASK               ((1 << RETRO_DEVICE_TYPE_SHIFT) - 1)
#define RETRO_DEVICE_SUBCLASS(base, id) (((id + 1) << RETRO_DEVICE_TYPE_SHIFT) | base)

#define RETRO_DEVICE_NONE               0
#define RETRO_DEVICE_JOYPAD             1
#define RETRO_DEVICE_MOUSE              2
#define RETRO_DEVICE_KEYBOARD           3
#define RETRO_DEVICE_LIGHTGUN           4
#define RETRO_DEVICE_ANALOG             5
#define RETRO_DEVICE_POINTER            6

#define RETRO_DEVICE_ID_JOYPAD_B        0
#define RETRO_DEVICE_ID_JOYPAD_Y        1
#define RETRO_DEVICE_ID_JOYPAD_SELECT   2
#define RETRO_DEVICE_ID_JOYPAD_START    3
#define RETRO_DEVICE_ID_JOYPAD_UP       4
#define RETRO_DEVICE_ID_JOYPAD_DOWN     5
#define RETRO_DEVICE_ID_JOYPAD_LEFT     6
#define RETRO_DEVICE_ID_JOYPAD_RIGHT    7
#define RETRO_DEVICE_ID_JOYPAD_A        8
#define RETRO_DEVICE_ID_JOYPAD_X        9
#define RETRO_DEVICE_ID_JOYPAD_L       10
#define RETRO_DEVICE_ID_JOYPAD_R       11
#define RETRO_DEVICE_ID_JOYPAD_L2      12
#define RETRO_DEVICE_ID_JOYPAD_R2      13
#define RETRO_DEVICE_ID_JOYPAD_L3      14
#define RETRO_DEVICE_ID_JOYPAD_R3      15
#define RETRO_DEVICE_ID_JOYPAD_MASK   256

#define RETRO_DEVICE_INDEX_ANALOG_LEFT   0
#define RETRO_DEVICE_INDEX_ANALOG_RIGHT  1
#define RETRO_DEVICE_INDEX_ANALOG_BUTTON 2
#define RETRO_DEVICE_ID_ANALOG_X         0
#define RETRO_DEVICE_ID_ANALOG_Y         1

#define RETRO_DEVICE_ID_MOUSE_X                0
#define RETRO_DEVICE_ID_MOUSE_Y                1
#define RETRO_DEVICE_ID_MOUSE_LEFT             2
#define RETRO_DEVICE_ID_MOUSE_RIGHT            3
#define RETRO_DEVICE_ID_MOUSE_WHEELUP          4
#define RETRO_DEVICE_ID_MOUSE_WHEELDOWN        5
#define RETRO_DEVICE_ID_MOUSE_MIDDLE           6
#define RETRO_DEVICE_ID_MOUSE_HORIZ_WHEELUP    7
#define RETRO_DEVICE_ID_MOUSE_HORIZ_WHEELDOWN  8
#define RETRO_DEVICE_ID_MOUSE_BUTTON_4         9
#define RETRO_DEVICE_ID_MOUSE_BUTTON_5        10

#define RETRO_DEVICE_ID_LIGHTGUN_SCREEN_X     13
#define RETRO_DEVICE_ID_LIGHTGUN_SCREEN_Y     14
#define RETRO_DEVICE_ID_LIGHTGUN_IS_OFFSCREEN 15
#define RETRO_DEVICE_ID_LIGHTGUN_TRIGGER       2
#define RETRO_DEVICE_ID_LIGHTGUN_RELOAD       16
#define RETRO_DEVICE_ID_LIGHTGUN_AUX_A         3
#define RETRO_DEVICE_ID_LIGHTGUN_AUX_B         4
#define RETRO_DEVICE_ID_LIGHTGUN_START         6
#define RETRO_DEVICE_ID_LIGHTGUN_SELECT        7

#define RETRO_DEVICE_ID_POINTER_X         0
#define RETRO_DEVICE_ID_POINTER_Y         1
#define RETRO_DEVICE_ID_POINTER_PRESSED   2
#define RETRO_DEVICE_ID_POINTER_COUNT     3

#define RETRO_REGION_NTSC 0
#define RETRO_REGION_PAL  1

#define RETRO_MEMORY_MASK       0xff
#define RETRO_MEMORY_SAVE_RAM   0
#define RETRO_MEMORY_RTC        1
#define RETRO_MEMORY_SYSTEM_RAM 2
#define RETRO_MEMORY_VIDEO_RAM  3

#define RETRO_ENVIRONMENT_EXPERIMENTAL 0x10000
#define RETRO_ENVIRONMENT_PRIVATE      0x20000

#define RETRO_ENVIRONMENT_SET_ROTATION                    1
#define RETRO_ENVIRONMENT_GET_OVERSCAN                    2
#define RETRO_ENVIRONMENT_GET_CAN_DUPE                    3
#define RETRO_ENVIRONMENT_SET_MESSAGE                     6
#define RETRO_ENVIRONMENT_SHUTDOWN                        7
#define RETRO_ENVIRONMENT_SET_PERFORMANCE_LEVEL           8
#define RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY            9
#define RETRO_ENVIRONMENT_SET_PIXEL_FORMAT               10
#define RETRO_ENVIRONMENT_SET_INPUT_DESCRIPTORS          11
#define RETRO_ENVIRONMENT_SET_KEYBOARD_CALLBACK          12
#define RETRO_ENVIRONMENT_SET_DISK_CONTROL_INTERFACE     13
#define RETRO_ENVIRONMENT_SET_HW_RENDER                  14
#define RETRO_ENVIRONMENT_GET_VARIABLE                   15
#define RETRO_ENVIRONMENT_SET_VARIABLES                  16
#define RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE            17
#define RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME            18
#define RETRO_ENVIRONMENT_GET_LIBRETRO_PATH              19
#define RETRO_ENVIRONMENT_SET_FRAME_TIME_CALLBACK        21
#define RETRO_ENVIRONMENT_SET_AUDIO_CALLBACK             22
#define RETRO_ENVIRONMENT_GET_RUMBLE_INTERFACE           23
#define RETRO_ENVIRONMENT_GET_INPUT_DEVICE_CAPABILITIES  24
#define RETRO_ENVIRONMENT_GET_SENSOR_INTERFACE           (25 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_CAMERA_INTERFACE           (26 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_LOG_INTERFACE              27
#define RETRO_ENVIRONMENT_GET_PERF_INTERFACE             28
#define RETRO_ENVIRONMENT_GET_LOCATION_INTERFACE         29
#define RETRO_ENVIRONMENT_GET_CORE_ASSETS_DIRECTORY      30
#define RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY             31
#define RETRO_ENVIRONMENT_SET_SYSTEM_AV_INFO             32
#define RETRO_ENVIRONMENT_SET_PROC_ADDRESS_CALLBACK      33
#define RETRO_ENVIRONMENT_SET_SUBSYSTEM_INFO             34
#define RETRO_ENVIRONMENT_SET_CONTROLLER_INFO            35
#define RETRO_ENVIRONMENT_SET_MEMORY_MAPS                (36 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_SET_GEOMETRY                   37
#define RETRO_ENVIRONMENT_GET_USERNAME                   38
#define RETRO_ENVIRONMENT_GET_LANGUAGE                   39
#define RETRO_ENVIRONMENT_GET_CURRENT_SOFTWARE_FRAMEBUFFER (40 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_HW_RENDER_INTERFACE        (41 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_SET_SUPPORT_ACHIEVEMENTS       (42 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_SET_HW_RENDER_CONTEXT_NEGOTIATION_INTERFACE (43 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_SET_SERIALIZATION_QUIRKS       44
#define RETRO_ENVIRONMENT_SET_HW_SHARED_CONTEXT          (45 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_VFS_INTERFACE              (46 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_LED_INTERFACE              (47 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_AUDIO_VIDEO_ENABLE         (48 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_MIDI_INTERFACE             (49 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_FASTFORWARDING             (50 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_TARGET_REFRESH_RATE        (51 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_INPUT_BITMASKS             (52 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_CORE_OPTIONS_VERSION       52
#define RETRO_ENVIRONMENT_SET_CORE_OPTIONS               53
#define RETRO_ENVIRONMENT_SET_CORE_OPTIONS_INTL          54
#define RETRO_ENVIRONMENT_SET_CORE_OPTIONS_DISPLAY       55
#define RETRO_ENVIRONMENT_GET_PREFERRED_HW_RENDER        56
#define RETRO_ENVIRONMENT_GET_DISK_CONTROL_INTERFACE_VERSION 57
#define RETRO_ENVIRONMENT_SET_DISK_CONTROL_EXT_INTERFACE 58
#define RETRO_ENVIRONMENT_GET_MESSAGE_INTERFACE_VERSION  59
#define RETRO_ENVIRONMENT_SET_MESSAGE_EXT                60
#define RETRO_ENVIRONMENT_GET_INPUT_MAX_USERS            61
#define RETRO_ENVIRONMENT_SET_AUDIO_BUFFER_STATUS_CALLBACK 62
#define RETRO_ENVIRONMENT_SET_MINIMUM_AUDIO_LATENCY      63
#define RETRO_ENVIRONMENT_SET_FASTFORWARDING_OVERRIDE    64
#define RETRO_ENVIRONMENT_SET_CONTENT_INFO_OVERRIDE      65
#define RETRO_ENVIRONMENT_GET_GAME_INFO_EXT              66
#define RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2            67
#define RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2_INTL       68
#define RETRO_ENVIRONMENT_SET_CORE_OPTIONS_UPDATE_DISPLAY_CALLBACK 69
#define RETRO_ENVIRONMENT_SET_VARIABLE                   70
#define RETRO_ENVIRONMENT_GET_THROTTLE_STATE             (71 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_SAVESTATE_CONTEXT          (72 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_HW_RENDER_CONTEXT_NEGOTIATION_INTERFACE_SUPPORT (73 | RETRO_ENVIRONMENT_EXPERIMENTAL)
#define RETRO_ENVIRONMENT_GET_JIT_CAPABLE                74

#define RETRO_HW_FRAME_BUFFER_VALID ((void*)-1)

enum retro_pixel_format {
    RETRO_PIXEL_FORMAT_0RGB1555 = 0,
    RETRO_PIXEL_FORMAT_XRGB8888 = 1,
    RETRO_PIXEL_FORMAT_RGB565   = 2,
    RETRO_PIXEL_FORMAT_UNKNOWN  = INT_MAX
};

enum retro_log_level {
    RETRO_LOG_DEBUG = 0,
    RETRO_LOG_INFO  = 1,
    RETRO_LOG_WARN  = 2,
    RETRO_LOG_ERROR = 3,
    RETRO_LOG_DUMMY = INT_MAX
};

enum retro_rumble_effect {
    RETRO_RUMBLE_STRONG = 0,
    RETRO_RUMBLE_WEAK   = 1,
    RETRO_RUMBLE_DUMMY  = INT_MAX
};

enum retro_language {
    RETRO_LANGUAGE_ENGLISH    = 0,
    RETRO_LANGUAGE_JAPANESE   = 1,
    RETRO_LANGUAGE_FRENCH     = 2,
    RETRO_LANGUAGE_SPANISH    = 3,
    RETRO_LANGUAGE_GERMAN     = 4,
    RETRO_LANGUAGE_ITALIAN    = 5,
    RETRO_LANGUAGE_DUTCH      = 6,
    RETRO_LANGUAGE_PORTUGUESE_BRAZIL = 7,
    RETRO_LANGUAGE_PORTUGUESE_PORTUGAL = 8,
    RETRO_LANGUAGE_RUSSIAN    = 9,
    RETRO_LANGUAGE_KOREAN     = 10,
    RETRO_LANGUAGE_CHINESE_TRADITIONAL = 11,
    RETRO_LANGUAGE_CHINESE_SIMPLIFIED  = 12,
    RETRO_LANGUAGE_DUMMY      = INT_MAX
};

enum retro_hw_context_type {
    RETRO_HW_CONTEXT_NONE             = 0,
    RETRO_HW_CONTEXT_OPENGL           = 1,
    RETRO_HW_CONTEXT_OPENGLES2        = 2,
    RETRO_HW_CONTEXT_OPENGL_CORE      = 3,
    RETRO_HW_CONTEXT_OPENGLES3        = 4,
    RETRO_HW_CONTEXT_OPENGLES_VERSION = 5,
    RETRO_HW_CONTEXT_VULKAN           = 6,
    RETRO_HW_CONTEXT_DIRECT3D         = 7,
    RETRO_HW_CONTEXT_DUMMY            = INT_MAX
};

struct retro_message {
    const char *msg;
    unsigned frames;
};

struct retro_input_descriptor {
    unsigned port;
    unsigned device;
    unsigned index;
    unsigned id;
    const char *description;
};

struct retro_system_info {
    const char *library_name;
    const char *library_version;
    const char *valid_extensions;
    bool need_fullpath;
    bool block_extract;
};

struct retro_game_geometry {
    unsigned base_width;
    unsigned base_height;
    unsigned max_width;
    unsigned max_height;
    float aspect_ratio;
};

struct retro_system_timing {
    double fps;
    double sample_rate;
};

struct retro_system_av_info {
    struct retro_game_geometry geometry;
    struct retro_system_timing timing;
};

struct retro_variable {
    const char *key;
    const char *value;
};

#define RETRO_NUM_CORE_OPTION_VALUES_MAX 128

struct retro_core_option_value {
    const char *value;
    const char *label;
};

struct retro_core_option_definition {
    const char *key;
    const char *desc;
    const char *info;
    struct retro_core_option_value values[RETRO_NUM_CORE_OPTION_VALUES_MAX];
    const char *default_value;
};

struct retro_core_options_intl {
    struct retro_core_option_definition *us;
    struct retro_core_option_definition *local;
};

struct retro_core_option_v2_category {
    const char *key;
    const char *desc;
    const char *info;
};

struct retro_core_option_v2_definition {
    const char *key;
    const char *desc;
    const char *desc_categorized;
    const char *info;
    const char *info_categorized;
    const char *category_key;
    struct retro_core_option_value values[RETRO_NUM_CORE_OPTION_VALUES_MAX];
    const char *default_value;
};

struct retro_core_options_v2 {
    struct retro_core_option_v2_category *categories;
    struct retro_core_option_v2_definition *definitions;
};

struct retro_core_options_v2_intl {
    struct retro_core_options_v2 *us;
    struct retro_core_options_v2 *local;
};

struct retro_game_info {
    const char *path;
    const void *data;
    size_t size;
    const char *meta;
};

typedef void (RETRO_CALLCONV *retro_log_printf_t)(enum retro_log_level level, const char *fmt, ...);

struct retro_log_callback {
    retro_log_printf_t log;
};

typedef bool (RETRO_CALLCONV *retro_set_rumble_state_t)(unsigned port, enum retro_rumble_effect effect, uint16_t strength);

struct retro_rumble_interface {
    retro_set_rumble_state_t set_rumble_state;
};

typedef int64_t retro_time_t;
typedef uint64_t retro_perf_tick_t;

struct retro_perf_counter {
    const char *ident;
    retro_perf_tick_t start;
    retro_perf_tick_t total;
    retro_perf_tick_t call_cnt;
    bool registered;
};

typedef retro_time_t (RETRO_CALLCONV *retro_perf_get_time_usec_t)(void);
typedef retro_perf_tick_t (RETRO_CALLCONV *retro_perf_get_counter_t)(void);
typedef uint64_t (RETRO_CALLCONV *retro_get_cpu_features_t)(void);
typedef void (RETRO_CALLCONV *retro_perf_log_t)(void);
typedef void (RETRO_CALLCONV *retro_perf_register_t)(struct retro_perf_counter *counter);
typedef void (RETRO_CALLCONV *retro_perf_start_t)(struct retro_perf_counter *counter);
typedef void (RETRO_CALLCONV *retro_perf_stop_t)(struct retro_perf_counter *counter);

struct retro_perf_callback {
    retro_perf_get_time_usec_t    get_time_usec;
    retro_get_cpu_features_t      get_cpu_features;
    retro_perf_get_counter_t      get_perf_counter;
    retro_perf_register_t         perf_register;
    retro_perf_start_t            perf_start;
    retro_perf_stop_t             perf_stop;
    retro_perf_log_t              perf_log;
};

struct retro_memory_descriptor {
    uint64_t flags;
    void *ptr;
    size_t offset;
    size_t start;
    size_t select;
    size_t disconnect;
    size_t len;
    const char *addrspace;
};

struct retro_memory_map {
    const struct retro_memory_descriptor *descriptors;
    unsigned num_descriptors;
};

struct retro_controller_description {
    const char *desc;
    unsigned id;
};

struct retro_controller_info {
    const struct retro_controller_description *types;
    unsigned num_types;
};

typedef void (RETRO_CALLCONV *retro_proc_address_t)(void);
typedef retro_proc_address_t (RETRO_CALLCONV *retro_hw_get_proc_address_t)(const char *sym);
typedef uintptr_t (RETRO_CALLCONV *retro_hw_get_current_framebuffer_t)(void);
typedef void (RETRO_CALLCONV *retro_hw_context_reset_t)(void);

struct retro_hw_render_callback {
    enum retro_hw_context_type context_type;
    retro_hw_context_reset_t context_reset;
    retro_hw_get_current_framebuffer_t get_current_framebuffer;
    retro_hw_get_proc_address_t get_proc_address;
    bool depth;
    bool stencil;
    bool bottom_left_origin;
    unsigned version_major;
    unsigned version_minor;
    bool cache_context;
    retro_hw_context_reset_t context_destroy;
    bool debug_context;
};

typedef void (RETRO_CALLCONV *retro_keyboard_event_t)(bool down, unsigned keycode, uint32_t character, uint16_t key_modifiers);

struct retro_keyboard_callback {
    retro_keyboard_event_t callback;
};

typedef int64_t retro_usec_t;
typedef void (RETRO_CALLCONV *retro_frame_time_callback_t)(retro_usec_t usec);

struct retro_frame_time_callback {
    retro_frame_time_callback_t callback;
    retro_usec_t reference;
};

typedef void (RETRO_CALLCONV *retro_audio_callback_t)(void);
typedef void (RETRO_CALLCONV *retro_audio_set_state_callback_t)(bool enabled);

struct retro_audio_callback {
    retro_audio_callback_t callback;
    retro_audio_set_state_callback_t set_state;
};

typedef bool (RETRO_CALLCONV *retro_environment_t)(unsigned cmd, void *data);
typedef void (RETRO_CALLCONV *retro_video_refresh_t)(const void *data, unsigned width, unsigned height, size_t pitch);
typedef void (RETRO_CALLCONV *retro_audio_sample_t)(int16_t left, int16_t right);
typedef size_t (RETRO_CALLCONV *retro_audio_sample_batch_t)(const int16_t *data, size_t frames);
typedef void (RETRO_CALLCONV *retro_input_poll_t)(void);
typedef int16_t (RETRO_CALLCONV *retro_input_state_t)(unsigned port, unsigned device, unsigned index, unsigned id);

typedef void (RETRO_CALLCONV *retro_set_environment_fn)(retro_environment_t);
typedef void (RETRO_CALLCONV *retro_set_video_refresh_fn)(retro_video_refresh_t);
typedef void (RETRO_CALLCONV *retro_set_audio_sample_fn)(retro_audio_sample_t);
typedef void (RETRO_CALLCONV *retro_set_audio_sample_batch_fn)(retro_audio_sample_batch_t);
typedef void (RETRO_CALLCONV *retro_set_input_poll_fn)(retro_input_poll_t);
typedef void (RETRO_CALLCONV *retro_set_input_state_fn)(retro_input_state_t);
typedef void (RETRO_CALLCONV *retro_init_fn)(void);
typedef void (RETRO_CALLCONV *retro_deinit_fn)(void);
typedef unsigned (RETRO_CALLCONV *retro_api_version_fn)(void);
typedef void (RETRO_CALLCONV *retro_get_system_info_fn)(struct retro_system_info *info);
typedef void (RETRO_CALLCONV *retro_get_system_av_info_fn)(struct retro_system_av_info *info);
typedef void (RETRO_CALLCONV *retro_set_controller_port_device_fn)(unsigned port, unsigned device);
typedef void (RETRO_CALLCONV *retro_reset_fn)(void);
typedef void (RETRO_CALLCONV *retro_run_fn)(void);
typedef size_t (RETRO_CALLCONV *retro_serialize_size_fn)(void);
typedef bool (RETRO_CALLCONV *retro_serialize_fn)(void *data, size_t size);
typedef bool (RETRO_CALLCONV *retro_unserialize_fn)(const void *data, size_t size);
typedef void (RETRO_CALLCONV *retro_cheat_reset_fn)(void);
typedef void (RETRO_CALLCONV *retro_cheat_set_fn)(unsigned index, bool enabled, const char *code);
typedef bool (RETRO_CALLCONV *retro_load_game_fn)(const struct retro_game_info *game);
typedef bool (RETRO_CALLCONV *retro_load_game_special_fn)(unsigned game_type, const struct retro_game_info *info, size_t num_info);
typedef void (RETRO_CALLCONV *retro_unload_game_fn)(void);
typedef unsigned (RETRO_CALLCONV *retro_get_region_fn)(void);
typedef void *(RETRO_CALLCONV *retro_get_memory_data_fn)(unsigned id);
typedef size_t (RETRO_CALLCONV *retro_get_memory_size_fn)(unsigned id);

#ifdef __cplusplus
}
#endif

#endif /* STORM_LIBRETRO_H__ */
