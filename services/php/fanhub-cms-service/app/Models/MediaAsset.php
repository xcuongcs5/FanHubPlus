<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Str;

class MediaAsset extends Model
{
    use HasFactory;

    protected $table = 'cms_media_assets';

    protected $keyType = 'string';
    public $incrementing = false;

    const UPDATED_AT = null;

    protected $fillable = [
        'id',
        'content_id',
        'file_url',
        'file_type',
        'sort_order',
    ];

    protected static function booted(): void
    {
        static::creating(function (MediaAsset $asset) {
            if (empty($asset->id)) {
                $asset->id = 'med_' . Str::lower(Str::random(12));
            }
        });
    }

    public function content()
    {
        return $this->belongsTo(Post::class, 'content_id');
    }
}
